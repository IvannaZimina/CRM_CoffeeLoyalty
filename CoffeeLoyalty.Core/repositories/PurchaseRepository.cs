using System.Data;
using System.Globalization;
using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.resources;
using Microsoft.Data.Sqlite;

namespace CoffeeLoyalty.Core.Repositories;

// Executes SQLite data access operations for receipt headers (PURCHASES) and implements IPurchaseRepository.
// Uses BaseRepository connection helpers and supports external SQLite transactions for atomic checkout.
public class PurchaseRepository : BaseRepository, IPurchaseRepository
{
    // Passes SqliteConnectionFactory to BaseRepository so connections open with foreign keys enabled.
    public PurchaseRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    // Inserts a new purchase header into PURCHASES and sets the generated Id on the Purchase model.
    public async Task<OperationResult<Purchase>> AddAsync(
        Purchase purchase,
        object? transaction = null,
        CancellationToken cancellationToken = default)
    {
        // Reject null Purchase before opening a connection or running SQL.
        if (purchase is null)
        {
            return OperationResult<Purchase>.Failure(
                CoreResources.ValRequiredField,
                "INVALID_PURCHASE");
        }

        // Parameterized INSERT with SELECT last_insert_rowid() to retrieve the new primary key.
        const string insertSql = """
            INSERT INTO PURCHASES (
                cardID,
                employeeID,
                totalAmount,
                bonusesSpent,
                bonusesEarned,
                timeStamp
            )
            VALUES (
                @cardId,
                @employeeId,
                @totalAmount,
                @bonusesSpent,
                @bonusesEarned,
                @timeStamp
            );
            SELECT last_insert_rowid();
            """;

        try
        {
            // Reuse an external SqliteTransaction if provided; otherwise open a standalone connection via BaseRepository.
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
                    return OperationResult<Purchase>.Failure(
                        CoreResources.PurchaseExternalTransactionInvalid,
                        "INVALID_TRANSACTION");
                }

                connection = externalTransaction.Connection;
            }

            try
            {
                // Attach external transaction to the command when participating in an atomic checkout transaction.
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = insertSql;
                if (externalTransaction is not null)
                {
                    command.Transaction = externalTransaction;
                }

                // Bind parameters with invariant formatting for decimal and ISO-8601 timestamp.
                command.Parameters.Add("@cardId", SqliteType.Integer).Value =
                    purchase.CardId.HasValue ? purchase.CardId.Value : DBNull.Value;
                command.Parameters.Add("@employeeId", SqliteType.Integer).Value = purchase.EmployeeId;
                command.Parameters.Add("@totalAmount", SqliteType.Text).Value =
                    purchase.TotalAmount.ToString(CultureInfo.InvariantCulture);
                command.Parameters.Add("@bonusesSpent", SqliteType.Integer).Value = purchase.BonusesSpent;
                command.Parameters.Add("@bonusesEarned", SqliteType.Integer).Value = purchase.BonusesEarned;
                command.Parameters.Add("@timeStamp", SqliteType.Text).Value = purchase.TimeStamp.ToString("O");

                // Execute query, assign generated ID back to purchase.Id, and return the saved record.
                object? scalarId = await command.ExecuteScalarAsync(cancellationToken);
                int createdId = Convert.ToInt32(scalarId, CultureInfo.InvariantCulture);
                purchase.Id = createdId;

                return OperationResult<Purchase>.Success(purchase);
            }
            finally
            {
                // Dispose only connections opened directly by this method.
                if (ownsConnection)
                {
                    await connection.DisposeAsync();
                }
            }
        }
        catch (SqliteException ex)
        {
            return OperationResult<Purchase>.Failure(
                $"{CoreResources.ErrorDatabaseConnection} ({ex.Message})",
                "DB_ERROR");
        }
        catch (Exception ex)
        {
            return OperationResult<Purchase>.Failure(ex.Message, "PURCHASE_ADD_ERROR");
        }
    }

    // Retrieves a single purchase header by purchaseID using a parameterized query.
    public async Task<OperationResult<Purchase>> GetByIdAsync(
        int purchaseId,
        CancellationToken cancellationToken = default)
    {
        // Validate positive ID before querying SQLite.
        if (purchaseId <= 0)
        {
            return OperationResult<Purchase>.Failure(
                string.Format(CoreResources.PurchaseResultValPurchaseIdPositive, nameof(purchaseId), purchaseId),
                "INVALID_ID");
        }

        const string selectSql = """
            SELECT
                purchaseID,
                cardID,
                employeeID,
                totalAmount,
                bonusesSpent,
                bonusesEarned,
                timeStamp
            FROM PURCHASES
            WHERE purchaseID = @purchaseId
            LIMIT 1;
            """;

        try
        {
            // Open connection via BaseRepository and execute parameterized lookup.
            await using SqliteConnection connection = OpenConnection();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = selectSql;
            command.Parameters.Add("@purchaseId", SqliteType.Integer).Value = purchaseId;

            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return OperationResult<Purchase>.Failure(
                    CoreResources.PurchaseNotFound,
                    "NOT_FOUND");
            }

            // Convert row into Purchase entity via MapPurchase helper.
            Purchase purchase = MapPurchase(reader);
            return OperationResult<Purchase>.Success(purchase);
        }
        catch (SqliteException ex)
        {
            return OperationResult<Purchase>.Failure(
                $"{CoreResources.ErrorDatabaseConnection} ({ex.Message})",
                "DB_ERROR");
        }
        catch (Exception ex)
        {
            return OperationResult<Purchase>.Failure(ex.Message, "PURCHASE_GET_ERROR");
        }
    }

    // Retrieves all purchases for a customer (linked via CARDS) ordered newest first.
    public async Task<OperationResult<IReadOnlyList<Purchase>>> GetByCustomerAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        // Validate customerId before querying SQLite.
        if (customerId <= 0)
        {
            return OperationResult<IReadOnlyList<Purchase>>.Failure(
                string.Format(CoreResources.PurchaseInvalidCustomerId, customerId),
                "INVALID_ID");
        }

        // Join PURCHASES and CARDS to filter by customerID with a parameterized query.
        const string selectByCustomerSql = """
            SELECT
                p.purchaseID,
                p.cardID,
                p.employeeID,
                p.totalAmount,
                p.bonusesSpent,
                p.bonusesEarned,
                p.timeStamp
            FROM PURCHASES p
            INNER JOIN CARDS c ON c.cardID = p.cardID
            WHERE c.customerID = @customerId
            ORDER BY p.timeStamp DESC, p.purchaseID DESC;
            """;

        try
        {
            // Read and map all matching purchase rows into a list.
            await using SqliteConnection connection = OpenConnection();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = selectByCustomerSql;
            command.Parameters.Add("@customerId", SqliteType.Integer).Value = customerId;

            List<Purchase> purchases = new();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                purchases.Add(MapPurchase(reader));
            }

            return OperationResult<IReadOnlyList<Purchase>>.Success(purchases);
        }
        catch (SqliteException ex)
        {
            return OperationResult<IReadOnlyList<Purchase>>.Failure(
                $"{CoreResources.ErrorDatabaseConnection} ({ex.Message})",
                "DB_ERROR");
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<Purchase>>.Failure(ex.Message, "PURCHASE_GET_BY_CUSTOMER_ERROR");
        }
    }

    // Retrieves all purchase headers across the store history ordered newest first.
    public async Task<OperationResult<IReadOnlyList<Purchase>>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        const string selectAllSql = """
            SELECT
                purchaseID,
                cardID,
                employeeID,
                totalAmount,
                bonusesSpent,
                bonusesEarned,
                timeStamp
            FROM PURCHASES
            ORDER BY timeStamp DESC, purchaseID DESC;
            """;

        try
        {
            // Open connection via BaseRepository and map all rows from PURCHASES.
            await using SqliteConnection connection = OpenConnection();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = selectAllSql;

            List<Purchase> purchases = new();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                purchases.Add(MapPurchase(reader));
            }

            return OperationResult<IReadOnlyList<Purchase>>.Success(purchases);
        }
        catch (SqliteException ex)
        {
            return OperationResult<IReadOnlyList<Purchase>>.Failure(
                $"{CoreResources.ErrorDatabaseConnection} ({ex.Message})",
                "DB_ERROR");
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<Purchase>>.Failure(ex.Message, "PURCHASE_GET_ALL_ERROR");
        }
    }

    // Maps a single SqliteDataReader row into a validated Purchase domain object.
    private static Purchase MapPurchase(SqliteDataReader reader)
    {
        // Look up column positions by name.
        int idOrd = reader.GetOrdinal("purchaseID");
        int cardIdOrd = reader.GetOrdinal("cardID");
        int employeeIdOrd = reader.GetOrdinal("employeeID");
        int totalAmountOrd = reader.GetOrdinal("totalAmount");
        int bonusesSpentOrd = reader.GetOrdinal("bonusesSpent");
        int bonusesEarnedOrd = reader.GetOrdinal("bonusesEarned");
        int timeStampOrd = reader.GetOrdinal("timeStamp");

        // Handle nullable cardID (walk-in purchases have NULL cardID).
        int? cardId = reader.IsDBNull(cardIdOrd)
            ? null
            : reader.GetInt32(cardIdOrd);

        int employeeId = reader.GetInt32(employeeIdOrd);

        // Parse decimal and ISO-8601 DateTime consistently using InvariantCulture.
        decimal totalAmount = reader.GetFieldType(totalAmountOrd) == typeof(string)
            ? decimal.Parse(reader.GetString(totalAmountOrd), CultureInfo.InvariantCulture)
            : reader.GetDecimal(totalAmountOrd);

        int bonusesSpent = reader.GetInt32(bonusesSpentOrd);
        int bonusesEarned = reader.GetInt32(bonusesEarnedOrd);

        DateTime timeStamp = reader.GetFieldType(timeStampOrd) == typeof(string)
            ? DateTime.Parse(
                reader.GetString(timeStampOrd),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind)
            : reader.GetDateTime(timeStampOrd);

        // Create Purchase instance and assign persisted primary key Id.
        Purchase purchase = new(
            employeeId: employeeId,
            totalAmount: totalAmount,
            bonusesSpent: bonusesSpent,
            bonusesEarned: bonusesEarned,
            cardId: cardId,
            timeStamp: timeStamp)
        {
            Id = reader.GetInt32(idOrd)
        };

        return purchase;
    }
}