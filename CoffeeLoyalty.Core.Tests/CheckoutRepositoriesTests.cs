using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoffeeLoyalty.Core.Tests;

// Integration tests for PurchaseRepository, PurchaseItemRepository, and UserDrinkRepository against SQLite.
public class CheckoutRepositoriesTests
{
    // Verifies inserting walk-in and card-linked purchases, fetching by ID, filtering by customer, and listing all.
    [Fact]
    public async Task PurchaseRepository_AddAndQuery_WorksForWalkInAndCustomerPurchases()
    {
        await using TestSqliteContext db = await TestSqliteContext.CreateAsync();
        IPurchaseRepository repo = new PurchaseRepository(db.Factory);

        Purchase walkInPurchase = new(
            employeeId: 1,
            totalAmount: 5.00m,
            bonusesSpent: 0,
            bonusesEarned: 0,
            cardId: null,
            timeStamp: DateTime.UtcNow.AddMinutes(-10));

        Purchase cardPurchase = new(
            employeeId: 1,
            totalAmount: 8.00m,
            bonusesSpent: 10,
            bonusesEarned: 40,
            cardId: db.CardId,
            timeStamp: DateTime.UtcNow);

        // Check both receipts are inserted and receive generated IDs.
        OperationResult<Purchase> addWalkIn = await repo.AddAsync(walkInPurchase);
        OperationResult<Purchase> addCard = await repo.AddAsync(cardPurchase);
        Assert.True(addWalkIn.IsSuccess);
        Assert.True(addCard.IsSuccess);
        Assert.True(addWalkIn.Value!.Id > 0);
        Assert.True(addCard.Value!.Id > 0);

        // Check lookup by purchase ID.
        OperationResult<Purchase> byId = await repo.GetByIdAsync(addCard.Value.Id);
        Assert.True(byId.IsSuccess);
        Assert.Equal(db.CardId, byId.Value!.CardId);
        Assert.Equal(8.00m, byId.Value.TotalAmount);

        // Check customer history only returns purchases tied to that customer's card.
        OperationResult<IReadOnlyList<Purchase>> byCustomer = await repo.GetByCustomerAsync(db.CustomerId);
        Assert.True(byCustomer.IsSuccess);
        Purchase customerReceipt = Assert.Single(byCustomer.Value!);
        Assert.Equal(addCard.Value.Id, customerReceipt.Id);

        // Check full purchase list returns both records.
        OperationResult<IReadOnlyList<Purchase>> all = await repo.GetAllAsync();
        Assert.True(all.IsSuccess);
        Assert.Equal(2, all.Value!.Count);
    }

    // Verifies batch line-item persistence (AddRangeAsync), reading items by purchase ID, and empty-list rejection.
    [Fact]
    public async Task PurchaseItemRepository_AddRangeAndGetByPurchaseId_PersistsLineItems()
    {
        await using TestSqliteContext db = await TestSqliteContext.CreateAsync();
        IPurchaseRepository purchases = new PurchaseRepository(db.Factory);
        IPurchaseItemRepository itemsRepo = new PurchaseItemRepository(db.Factory);

        // Check empty item list is rejected.
        OperationResult emptyResult = await itemsRepo.AddRangeAsync([]);
        Assert.False(emptyResult.IsSuccess);

        OperationResult<Purchase> purchase = await purchases.AddAsync(new Purchase(
            employeeId: 1,
            totalAmount: 10.50m,
            bonusesSpent: 0,
            bonusesEarned: 50,
            cardId: db.CardId,
            timeStamp: DateTime.UtcNow));
        Assert.True(purchase.IsSuccess);

        PurchaseItem[] lineItems =
        [
            new(purchaseId: purchase.Value!.Id, drinkId: db.EspressoId, quantity: 2, unitPrice: 2.50m),
            new(purchaseId: purchase.Value.Id, drinkId: db.LatteId, quantity: 1, unitPrice: 5.50m)
        ];

        // Check batch insert and retrieval by purchase ID.
        OperationResult addRange = await itemsRepo.AddRangeAsync(lineItems);
        Assert.True(addRange.IsSuccess);

        OperationResult<IReadOnlyList<PurchaseItem>> saved = await itemsRepo.GetByPurchaseIdAsync(purchase.Value.Id);
        Assert.True(saved.IsSuccess);
        Assert.Equal(2, saved.Value!.Count);
        Assert.Equal(2, saved.Value[0].Quantity);
        Assert.Equal(5.50m, saved.Value[1].UnitPrice);
    }

    // Verifies inserting and incrementing per-card drink counters (UpsertQuantityAsync) and querying by customer.
    [Fact]
    public async Task UserDrinkRepository_UpsertQuantity_InsertsAndIncrementsCounters()
    {
        await using TestSqliteContext db = await TestSqliteContext.CreateAsync();
        IUserDrinkRepository userDrinks = new UserDrinkRepository(db.Factory);

        // First purchase creates a new counter row with quantity = 1.
        OperationResult<UserDrink> first = await userDrinks.UpsertQuantityAsync(
            cardId: db.CardId,
            drinkId: db.EspressoId,
            quantityDelta: 1);
        Assert.True(first.IsSuccess);
        Assert.Equal(1, first.Value!.Quantity);

        // Subsequent purchase increments existing counter by 3 to 4.
        OperationResult<UserDrink> second = await userDrinks.UpsertQuantityAsync(
            cardId: db.CardId,
            drinkId: db.EspressoId,
            quantityDelta: 3);
        Assert.True(second.IsSuccess);
        Assert.Equal(4, second.Value!.Quantity);

        // Check customer counter query reflects accumulated quantity.
        OperationResult<IReadOnlyList<UserDrink>> byCustomer = await userDrinks.GetByCustomerIdAsync(db.CustomerId);
        Assert.True(byCustomer.IsSuccess);
        UserDrink counter = Assert.Single(byCustomer.Value!);
        Assert.Equal(db.EspressoId, counter.DrinkId);
        Assert.Equal(4, counter.Quantity);
    }

    // Temporary SQLite database context initialized with project schemas and seed data.
    private sealed class TestSqliteContext : IAsyncDisposable
    {
        private readonly string _dbPath;

        public SqliteConnectionFactory Factory { get; }
        public int CustomerId { get; } = 10;
        public int CardId { get; } = 100;
        public int EspressoId { get; } = 1;
        public int LatteId { get; } = 2;

        private TestSqliteContext(string dbPath, SqliteConnectionFactory factory)
        {
            _dbPath = dbPath;
            Factory = factory;
        }

        public static async Task<TestSqliteContext> CreateAsync()
        {
            string path = Path.Combine(Path.GetTempPath(), $"coffeeloyalty_repo_{Guid.NewGuid():N}.db");
            SqliteConnectionFactory factory = new(path);

            await using SqliteConnection connection = factory.OpenConnection();
            string[] schemas =
            [
                "CategoriesSchema.sql",
                "CustomersSchema.sql",
                "CardsSchema.sql",
                "EmployeesSchema.sql",
                "DrinksSchema.sql",
                "PurchasesSchema.sql",
                "PurchaseItemSchema.sql",
                "UserDrinkSchema.sql",
                "LogsSchema.sql"
            ];

            foreach (string schema in schemas)
            {
                using Stream stream = typeof(DatabaseInitializer).Assembly
                    .GetManifestResourceStream($"CoffeeLoyalty.Core.data.schemas.{schema}")!;
                using StreamReader reader = new(stream);
                await using SqliteCommand cmd = connection.CreateCommand();
                cmd.CommandText = await reader.ReadToEndAsync();
                await cmd.ExecuteNonQueryAsync();
            }

            await using SqliteCommand seed = connection.CreateCommand();
            seed.CommandText = """
                INSERT INTO CATEGORIES (categoryID, categoryName, bonusAccrualRate, upgradeThreshold)
                VALUES (1, 'General', 5.0, 0);
                INSERT INTO EMPLOYEES (employeeID, employeeName, employeeRole, employeePassword, isActive)
                VALUES (1, 'Barista', 'Cashier', 'hash', 1);
                INSERT INTO DRINKS (drinkID, drinkName, drinkPrice, drinkType, isActive)
                VALUES
                    (1, 'Espresso', 2.50, 'Coffee', 1),
                    (2, 'Latte', 5.50, 'Coffee', 1);
                INSERT INTO CUSTOMERS (customerID, categoryID, customerName, bonusBalance, isActive, timeStamp)
                VALUES (10, 1, 'Client One', 25, 1, '2026-01-01T00:00:00Z');
                INSERT INTO CARDS (cardID, customerID, isActive)
                VALUES (100, 10, 1);
                """;
            await seed.ExecuteNonQueryAsync();

            return new TestSqliteContext(path, factory);
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }

            return ValueTask.CompletedTask;
        }
    }
}