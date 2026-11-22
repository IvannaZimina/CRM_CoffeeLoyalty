using System.Data;
using System.Globalization;
using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.resources;
using CoffeeLoyalty.Core.Validation;
using Microsoft.Data.Sqlite;

namespace CoffeeLoyalty.Core.Repositories;

// Executes SQLite data access operations for customer drink counters (USER_DRINK) and implements IUserDrinkRepository.
// Maps customerId through CARDS.cardID and supports external SQLite transactions during checkout.
public class UserDrinkRepository : BaseRepository, IUserDrinkRepository
{
    // Passes SqliteConnectionFactory to BaseRepository so connections open with foreign keys enabled.
    public UserDrinkRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    // Retrieves all drink counter records associated with the specified customerId via CARDS.
    public async Task<OperationResult<IReadOnlyList<UserDrink>>> GetByCustomerIdAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        // Reject non-positive customerId before opening a SQLite connection.
        if (!ValidationHelper.IsPositive(customerId))
        {
            return OperationResult<IReadOnlyList<UserDrink>>.Failure(
                string.Format(CoreResources.UserDrinkValCardIdPositive, nameof(customerId), customerId),
                "INVALID_CUSTOMER_ID");
        }

        // Join USER_DRINK with CARDS so counters can be filtered by customerID.
        const string selectSql = """
            SELECT
                ud.userDrinkID,
                ud.cardID,
                ud.drinkID,
                ud.quantity
            FROM USER_DRINK ud
            INNER JOIN CARDS c ON c.cardID = ud.cardID
            WHERE c.customerID = @customerId
            ORDER BY ud.quantity DESC, ud.drinkID ASC;
            """;

        try
        {
            // Open connection via BaseRepository and read all matching UserDrink counters.
            await using SqliteConnection connection = OpenConnection();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = selectSql;
            command.Parameters.Add("@customerId", SqliteType.Integer).Value = customerId;

            List<UserDrink> items = new();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapUserDrink(reader));
            }

            return OperationResult<IReadOnlyList<UserDrink>>.Success(items);
        }
        catch (SqliteException ex)
        {
            return OperationResult<IReadOnlyList<UserDrink>>.Failure(
                $"{CoreResources.ErrorDatabaseConnection} ({ex.Message})",
                "DB_ERROR");
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<UserDrink>>.Failure(ex.Message, "USER_DRINK_GET_ERROR");
        }
    }

    // Inserts a new (cardID, drinkID) row or increments quantity for an existing counter in USER_DRINK.
    public async Task<OperationResult<UserDrink>> UpsertQuantityAsync(
        int customerId,
        int drinkId,
        int quantityDelta,
        object? transaction = null,
        CancellationToken cancellationToken = default)
    {
        // Validate input identifiers and positive delta using ValidationHelper before touching SQLite.
        if (!ValidationHelper.IsPositive(customerId))
        {
            return OperationResult<UserDrink>.Failure(
                string.Format(CoreResources.UserDrinkValCardIdPositive, nameof(customerId), customerId),
                "INVALID_CUSTOMER_ID");
        }

        if (!ValidationHelper.IsPositive(drinkId))
        {
            return OperationResult<UserDrink>.Failure(
                string.Format(CoreResources.UserDrinkValDrinkIdPositive, nameof(drinkId), drinkId),
                "INVALID_DRINK_ID");
        }

        if (!ValidationHelper.IsPositive(quantityDelta))
        {
            return OperationResult<UserDrink>.Failure(
                string.Format(CoreResources.UserDrinkQuantityDeltaPositive, nameof(quantityDelta), quantityDelta),
                "INVALID_QUANTITY_DELTA");
        }

        // Reuse external SqliteTransaction if provided; otherwise open a connection via BaseRepository.
        SqliteTransaction? externalTransaction = transaction as SqliteTransaction;
        bool ownsConnection = externalTransaction is null;

        SqliteConnection connection;
        if (ownsConnection)
        {
            connection = OpenConnection();
        }
        else
        {
            if (externalTransaction?.Connection is null)
            {
                return OperationResult<UserDrink>.Failure(
                    CoreResources.UserDrinkExternalTransactionInvalid,
                    "INVALID_TRANSACTION");
            }

            connection = externalTransaction.Connection;
        }

        try
        {
            // Resolve the customer's cardID from CARDS so USER_DRINK foreign key and UNIQUE(cardID, drinkID) are satisfied.
            int? cardId = await ResolveCardIdForCustomerAsync(
                connection,
                externalTransaction,
                customerId,
                cancellationToken);

            if (!cardId.HasValue)
            {
                return OperationResult<UserDrink>.Failure(
                    string.Format(CoreResources.UserDrinkCardNotFoundForCustomer, customerId),
                    "CARD_NOT_FOUND");
            }

            // Upsert into USER_DRINK on conflict (cardID, drinkID), incrementing quantity by quantityDelta.
            const string upsertSql = """
                INSERT INTO USER_DRINK (
                    cardID,
                    drinkID,
                    quantity
                )
                VALUES (
                    @cardId,
                    @drinkId,
                    @quantityDelta
                )
                ON CONFLICT(cardID, drinkID) DO UPDATE SET
                    quantity = USER_DRINK.quantity + excluded.quantity;
                """;

            await using (SqliteCommand upsertCommand = connection.CreateCommand())
            {
                upsertCommand.CommandText = upsertSql;
                if (externalTransaction is not null)
                {
                    upsertCommand.Transaction = externalTransaction;
                }

                upsertCommand.Parameters.Add("@cardId", SqliteType.Integer).Value = cardId.Value;
                upsertCommand.Parameters.Add("@drinkId", SqliteType.Integer).Value = drinkId;
                upsertCommand.Parameters.Add("@quantityDelta", SqliteType.Integer).Value = quantityDelta;

                await upsertCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            // Read back the persisted row so the returned UserDrink has its current quantity and userDrinkID.
            const string selectUpdatedSql = """
                SELECT
                    userDrinkID,
                    cardID,
                    drinkID,
                    quantity
                FROM USER_DRINK
                WHERE cardID = @cardId AND drinkID = @drinkId
                LIMIT 1;
                """;

            await using SqliteCommand selectCommand = connection.CreateCommand();
            selectCommand.CommandText = selectUpdatedSql;
            if (externalTransaction is not null)
            {
                selectCommand.Transaction = externalTransaction;
            }

            selectCommand.Parameters.Add("@cardId", SqliteType.Integer).Value = cardId.Value;
            selectCommand.Parameters.Add("@drinkId", SqliteType.Integer).Value = drinkId;

            await using SqliteDataReader reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return OperationResult<UserDrink>.Failure(
                    CoreResources.ErrorDatabaseConnection,
                    "UPSERT_READ_FAILED");
            }

            UserDrink updatedRecord = MapUserDrink(reader);
            return OperationResult<UserDrink>.Success(updatedRecord);
        }
        catch (SqliteException ex)
        {
            return OperationResult<UserDrink>.Failure(
                $"{CoreResources.ErrorDatabaseConnection} ({ex.Message})",
                "DB_ERROR");
        }
        catch (Exception ex)
        {
            return OperationResult<UserDrink>.Failure(ex.Message, "USER_DRINK_UPSERT_ERROR");
        }
        finally
        {
            // Dispose only connections opened directly by UpsertQuantityAsync.
            if (ownsConnection)
            {
                await connection.DisposeAsync();
            }
        }
    }

    // Finds the cardID linked to customerId in CARDS (preferring an active card), or null when none exists.
    private static async Task<int?> ResolveCardIdForCustomerAsync(
        SqliteConnection connection,
        SqliteTransaction? activeTransaction,
        int customerId,
        CancellationToken cancellationToken)
    {
        const string selectCardSql = """
            SELECT cardID
            FROM CARDS
            WHERE customerID = @customerId
            ORDER BY isActive DESC, cardID ASC
            LIMIT 1;
            """;

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = selectCardSql;
        if (activeTransaction is not null)
        {
            command.Transaction = activeTransaction;
        }

        command.Parameters.Add("@customerId", SqliteType.Integer).Value = customerId;

        object? scalar = await command.ExecuteScalarAsync(cancellationToken);
        if (scalar is null || scalar == DBNull.Value)
        {
            return null;
        }

        return Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
    }

    // Maps a single SqliteDataReader row from USER_DRINK into a validated UserDrink model.
    private static UserDrink MapUserDrink(SqliteDataReader reader)
    {
        // Resolve column ordinals once for clear and type-safe column reading.
        int idOrd = reader.GetOrdinal("userDrinkID");
        int cardIdOrd = reader.GetOrdinal("cardID");
        int drinkIdOrd = reader.GetOrdinal("drinkID");
        int quantityOrd = reader.GetOrdinal("quantity");

        UserDrink record = new(
            cardId: reader.GetInt32(cardIdOrd),
            drinkId: reader.GetInt32(drinkIdOrd),
            quantity: reader.GetInt32(quantityOrd))
        {
            Id = reader.GetInt32(idOrd)
        };

        return record;
    }
}