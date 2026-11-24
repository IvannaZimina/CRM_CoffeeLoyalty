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

// Executes SQLite data access operations for card drink counters (USER_DRINK) and implements IUserDrinkRepository.
// Updates counters directly by cardID and supports external SQLite transactions during checkout.
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
                string.Format(CoreResources.PurchaseInvalidCustomerId, customerId),
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SqliteException ex)
        {
            System.Diagnostics.Trace.TraceWarning("UserDrinkRepository: {0}", ex.GetType().Name);
            return OperationResult<IReadOnlyList<UserDrink>>.Failure(
                CoreResources.ErrorDatabaseConnection,
                "DB_ERROR");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("UserDrinkRepository: {0}", ex.GetType().Name);
            return OperationResult<IReadOnlyList<UserDrink>>.Failure(CoreResources.ErrorUserDrinkReadFailed, "USER_DRINK_GET_ERROR");
        }
    }

    // Inserts a new (cardID, drinkID) row or increments quantity for an existing counter in USER_DRINK.
    public async Task<OperationResult<UserDrink>> UpsertQuantityAsync(
        int cardId,
        int drinkId,
        int quantityDelta,
        object? transaction = null,
        CancellationToken cancellationToken = default)
    {
        // Validate input identifiers and positive delta using ValidationHelper before touching SQLite.
        if (!ValidationHelper.IsPositive(cardId))
        {
            return OperationResult<UserDrink>.Failure(
                string.Format(CoreResources.UserDrinkValCardIdPositive, nameof(cardId), cardId),
                "INVALID_CARD_ID");
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
            // Upsert into USER_DRINK on conflict (cardID, drinkID), incrementing quantity by quantityDelta.
            // Foreign key on cardID/drinkID enforces that referenced rows exist in CARDS and DRINKS.
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

                upsertCommand.Parameters.Add("@cardId", SqliteType.Integer).Value = cardId;
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

            selectCommand.Parameters.Add("@cardId", SqliteType.Integer).Value = cardId;
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SqliteException ex)
        {
            System.Diagnostics.Trace.TraceWarning("UserDrinkRepository: {0}", ex.GetType().Name);
            return OperationResult<UserDrink>.Failure(
                CoreResources.ErrorDatabaseConnection,
                "DB_ERROR");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("UserDrinkRepository: {0}", ex.GetType().Name);
            return OperationResult<UserDrink>.Failure(CoreResources.UserDrinkUpdateFailed, "USER_DRINK_UPSERT_ERROR");
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