using System.Data;
using System.Globalization;
using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.resources;
using Microsoft.Data.Sqlite;

namespace CoffeeLoyalty.Core.Repositories;

// Executes SQLite data access operations for receipt line items (PURCHASE_ITEM) and implements IPurchaseItemRepository.
// Uses BaseRepository connection helpers and supports external SQLite transactions for atomic checkout.
public class PurchaseItemRepository : BaseRepository, IPurchaseItemRepository
{
    // Passes SqliteConnectionFactory to BaseRepository so connections open with foreign keys enabled.
    public PurchaseItemRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    // Inserts a single receipt line item into PURCHASE_ITEM and assigns the generated Id back to item.
    public async Task<OperationResult<PurchaseItem>> AddAsync(
        PurchaseItem item,
        object? transaction = null,
        CancellationToken cancellationToken = default)
    {
        // Reject null PurchaseItem before opening a connection or running SQL.
        if (item is null)
        {
            return OperationResult<PurchaseItem>.Failure(
                CoreResources.ValRequiredField,
                "INVALID_PURCHASE_ITEM");
        }

        // Reuse an external SqliteTransaction when participating in a checkout transaction; otherwise open a connection.
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
                return OperationResult<PurchaseItem>.Failure(
                    CoreResources.PurchaseItemExternalTransactionInvalid,
                    "INVALID_TRANSACTION");
            }

            connection = externalTransaction.Connection;
        }

        try
        {
            // Insert the line item and read back the generated purchaseItemID.
            int createdId = await InsertItemCoreAsync(
                connection,
                externalTransaction,
                item,
                cancellationToken);

            item.Id = createdId;
            return OperationResult<PurchaseItem>.Success(item);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SqliteException ex)
        {
            System.Diagnostics.Trace.TraceWarning("PurchaseItemRepository: {0}", ex.GetType().Name);
            return OperationResult<PurchaseItem>.Failure(
                CoreResources.ErrorDatabaseConnection,
                "DB_ERROR");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("PurchaseItemRepository: {0}", ex.GetType().Name);
            return OperationResult<PurchaseItem>.Failure(CoreResources.PurchaseItemsSaveFailed, "PURCHASE_ITEM_ADD_ERROR");
        }
        finally
        {
            // Dispose only connections opened directly by AddAsync.
            if (ownsConnection)
            {
                await connection.DisposeAsync();
            }
        }
    }

    // Inserts multiple receipt line items atomically, reusing an external transaction or managing a local one.
    public async Task<OperationResult> AddRangeAsync(
        IEnumerable<PurchaseItem> items,
        object? transaction = null,
        CancellationToken cancellationToken = default)
    {
        // Guard against null collection before materializing items.
        if (items is null)
        {
            return OperationResult.Failure(
                CoreResources.ValRequiredField,
                "INVALID_PURCHASE_ITEMS");
        }

        // Materialize once so we can check emptiness and reject null entries before touching SQLite.
        List<PurchaseItem> itemList = items.ToList();
        if (itemList.Count == 0)
        {
            return OperationResult.Failure(
                CoreResources.PurchaseItemCollectionEmpty,
                "EMPTY_PURCHASE_ITEMS");
        }

        if (itemList.Any(x => x is null))
        {
            return OperationResult.Failure(
                CoreResources.ValRequiredField,
                "INVALID_PURCHASE_ITEM");
        }

        SqliteTransaction? externalTransaction = transaction as SqliteTransaction;

        try
        {
            if (externalTransaction is not null)
            {
                // Participate directly in the caller's transaction so header, items, and bonuses commit or roll back together.
                if (externalTransaction.Connection is null)
                {
                    return OperationResult.Failure(
                        CoreResources.PurchaseItemExternalTransactionInvalid,
                        "INVALID_TRANSACTION");
                }

                foreach (PurchaseItem item in itemList)
                {
                    int createdId = await InsertItemCoreAsync(
                        externalTransaction.Connection,
                        externalTransaction,
                        item,
                        cancellationToken);

                    item.Id = createdId;
                }

                return OperationResult.Success();
            }

            // When no external transaction is passed, run all inserts inside a local transaction.
            await ExecuteInTransactionAsync(async (connection, localTransaction, ct) =>
            {
                foreach (PurchaseItem item in itemList)
                {
                    int createdId = await InsertItemCoreAsync(
                        connection,
                        localTransaction,
                        item,
                        ct);

                    item.Id = createdId;
                }

                return true;
            }, cancellationToken);

            return OperationResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SqliteException ex)
        {
            System.Diagnostics.Trace.TraceWarning("PurchaseItemRepository: {0}", ex.GetType().Name);
            return OperationResult.Failure(
                CoreResources.ErrorDatabaseConnection,
                "DB_ERROR");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("PurchaseItemRepository: {0}", ex.GetType().Name);
            return OperationResult.Failure(CoreResources.PurchaseItemsSaveFailed, "PURCHASE_ITEM_ADD_RANGE_ERROR");
        }
    }

    // Retrieves all receipt line items for a given purchaseID using a parameterized query.
    public async Task<OperationResult<IReadOnlyList<PurchaseItem>>> GetByPurchaseIdAsync(
        int purchaseId,
        CancellationToken cancellationToken = default)
    {
        // Validate positive purchaseId before opening a SQLite connection.
        if (purchaseId <= 0)
        {
            return OperationResult<IReadOnlyList<PurchaseItem>>.Failure(
                string.Format(CoreResources.PurchaseItemValPurchaseIdPositive, nameof(purchaseId), purchaseId),
                "INVALID_ID");
        }

        const string selectSql = """
            SELECT
                purchaseItemID,
                purchaseID,
                drinkID,
                quantity,
                unitPrice
            FROM PURCHASE_ITEM
            WHERE purchaseID = @purchaseId
            ORDER BY purchaseItemID ASC;
            """;

        try
        {
            // Open connection via BaseRepository and read all line items matching purchaseId.
            await using SqliteConnection connection = OpenConnection();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = selectSql;
            command.Parameters.Add("@purchaseId", SqliteType.Integer).Value = purchaseId;

            List<PurchaseItem> items = new();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapPurchaseItem(reader));
            }

            return OperationResult<IReadOnlyList<PurchaseItem>>.Success(items);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SqliteException ex)
        {
            System.Diagnostics.Trace.TraceWarning("PurchaseItemRepository: {0}", ex.GetType().Name);
            return OperationResult<IReadOnlyList<PurchaseItem>>.Failure(
                CoreResources.ErrorDatabaseConnection,
                "DB_ERROR");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("PurchaseItemRepository: {0}", ex.GetType().Name);
            return OperationResult<IReadOnlyList<PurchaseItem>>.Failure(CoreResources.ErrorPurchaseItemsReadFailed, "PURCHASE_ITEM_GET_ERROR");
        }
    }

    // Executes the parameterized INSERT into PURCHASE_ITEM and returns the newly generated row ID.
    private static async Task<int> InsertItemCoreAsync(
        SqliteConnection connection,
        SqliteTransaction? activeTransaction,
        PurchaseItem item,
        CancellationToken cancellationToken)
    {
        const string insertSql = """
            INSERT INTO PURCHASE_ITEM (
                purchaseID,
                drinkID,
                quantity,
                unitPrice
            )
            VALUES (
                @purchaseId,
                @drinkId,
                @quantity,
                @unitPrice
            );
            SELECT last_insert_rowid();
            """;

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = insertSql;
        if (activeTransaction is not null)
        {
            command.Transaction = activeTransaction;
        }

        // Bind parameters explicitly, matching REAL unitPrice in PURCHASE_ITEM schema.
        command.Parameters.Add("@purchaseId", SqliteType.Integer).Value = item.PurchaseId;
        command.Parameters.Add("@drinkId", SqliteType.Integer).Value = item.DrinkId;
        command.Parameters.Add("@quantity", SqliteType.Integer).Value = item.Quantity;
        command.Parameters.Add("@unitPrice", SqliteType.Real).Value =
            Convert.ToDouble(item.UnitPrice, CultureInfo.InvariantCulture);

        object? scalarId = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(scalarId, CultureInfo.InvariantCulture);
    }

    // Maps a single SqliteDataReader row from PURCHASE_ITEM into a validated PurchaseItem model.
    private static PurchaseItem MapPurchaseItem(SqliteDataReader reader)
    {
        // Resolve column ordinals once for clear and type-safe column reading.
        int idOrd = reader.GetOrdinal("purchaseItemID");
        int purchaseIdOrd = reader.GetOrdinal("purchaseID");
        int drinkIdOrd = reader.GetOrdinal("drinkID");
        int quantityOrd = reader.GetOrdinal("quantity");
        int unitPriceOrd = reader.GetOrdinal("unitPrice");

        int purchaseId = reader.GetInt32(purchaseIdOrd);
        int drinkId = reader.GetInt32(drinkIdOrd);
        int quantity = reader.GetInt32(quantityOrd);

        // Parse unitPrice consistently whether stored as REAL/numeric or text in SQLite.
        decimal unitPrice = reader.GetFieldType(unitPriceOrd) == typeof(string)
            ? decimal.Parse(reader.GetString(unitPriceOrd), CultureInfo.InvariantCulture)
            : reader.GetDecimal(unitPriceOrd);

        PurchaseItem item = new(
            purchaseId: purchaseId,
            drinkId: drinkId,
            quantity: quantity,
            unitPrice: unitPrice)
        {
            Id = reader.GetInt32(idOrd)
        };

        return item;
    }
}