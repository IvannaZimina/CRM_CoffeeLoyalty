using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Interfaces.Services;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.Repositories;
using CoffeeLoyalty.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoffeeLoyalty.Core.Tests;

// Unit and integration tests covering the Purchases section:
// 1) Purchase domain model validation and walk-in vs. card-linked initialization,
// 2) PurchaseRepository SQLite persistence and lookup queries,
// 3) PurchaseService end-to-end checkout, bonus updates, drink counters, and atomic rollback.
public class PurchaseTests
{
    // Tests Purchase domain model constructors and property guards for both walk-in and loyalty-card orders.
    [Fact]
    public void PurchaseModel_ValidatesFields_AndSupportsWalkInAndCard()
    {
        // Arrange: capture a reference timestamp for receipt creation.
        DateTime now = DateTime.UtcNow;

        // Act: construct a walk-in receipt where CardId is omitted (null).
        Purchase walkIn = new(
            employeeId: 2,
            totalAmount: 6.50m,
            bonusesSpent: 0,
            bonusesEarned: 0,
            cardId: null,
            timeStamp: now);

        // Assert: verify walk-in receipt stores null CardId and exact monetary/employee values.
        Assert.Null(walkIn.CardId);
        Assert.Equal(2, walkIn.EmployeeId);
        Assert.Equal(6.50m, walkIn.TotalAmount);

        // Act: construct a card-linked receipt with redeemed and earned loyalty points.
        Purchase withCard = new(
            employeeId: 2,
            totalAmount: 4.00m,
            bonusesSpent: 10,
            bonusesEarned: 20,
            cardId: 15,
            timeStamp: now);

        // Assert: verify loyalty card ID and bonus fields are preserved.
        Assert.Equal(15, withCard.CardId);
        Assert.Equal(10, withCard.BonusesSpent);
        Assert.Equal(20, withCard.BonusesEarned);

        // Assert: verify domain invariants reject non-positive IDs and negative amounts/bonuses.
        Assert.Throws<ArgumentOutOfRangeException>(() => new Purchase(0, 5.00m, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Purchase(2, -1.00m, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Purchase(2, 5.00m, -5, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Purchase(2, 5.00m, 0, -5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Purchase(2, 5.00m, 0, 0, cardId: 0));
    }

    // Tests PurchaseRepository SQLite persistence, primary key assignment, ID lookup, and NOT_FOUND handling.
    [Fact]
    public async Task PurchaseRepository_PersistsAndQueriesHeaders()
    {
        // Arrange: create an isolated SQLite database with seeded employee, card, and customer rows.
        await using PurchaseTestDb db = await PurchaseTestDb.CreateAsync(UserRole.Cashier);
        IPurchaseRepository repo = db.Purchases;

        Purchase purchase = new(
            employeeId: db.EmployeeId,
            totalAmount: 9.00m,
            bonusesSpent: 5,
            bonusesEarned: 45,
            cardId: db.CardId,
            timeStamp: DateTime.UtcNow);

        // Act: insert the purchase header into the PURCHASES table.
        OperationResult<Purchase> addResult = await repo.AddAsync(purchase);

        // Assert: verify insertion succeeded and SQLite generated a positive row ID.
        Assert.True(addResult.IsSuccess);
        Assert.True(addResult.Value!.Id > 0);

        // Act: retrieve the newly saved purchase header by its generated primary key.
        OperationResult<Purchase> fetched = await repo.GetByIdAsync(addResult.Value.Id);

        // Assert: verify all persisted columns round-trip accurately from SQLite.
        Assert.True(fetched.IsSuccess);
        Assert.Equal(9.00m, fetched.Value!.TotalAmount);
        Assert.Equal(5, fetched.Value.BonusesSpent);
        Assert.Equal(45, fetched.Value.BonusesEarned);

        // Act & Assert: query a non-existent purchase ID and verify NOT_FOUND failure result.
        OperationResult<Purchase> missing = await repo.GetByIdAsync(99999);
        Assert.False(missing.IsSuccess);
        Assert.Equal("NOT_FOUND", missing.ErrorCode);
    }

    // Tests end-to-end walk-in checkout through PurchaseService without a loyalty card.
    [Fact]
    public async Task PurchaseService_WalkInCheckout_CreatesReceiptAndItems()
    {
        // Arrange: spin up test database with an active Cashier session and two Espresso items in the cart.
        await using PurchaseTestDb db = await PurchaseTestDb.CreateAsync(UserRole.Cashier);

        PurchaseRequest request = new(
            employeeId: db.EmployeeId,
            items: [new CartItem(db.Espresso, 2)]);

        // Act: execute full checkout workflow.
        PurchaseResult result = await db.Service.ProcessPurchaseAsync(request);

        // Assert: verify receipt summary reflects 2 * 2.50 = 5.00 total and no loyalty bonus activity.
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.PurchaseId > 0);
        Assert.Equal(5.00m, result.TotalAmount);
        Assert.Equal(0, result.BonusesSpent);
        Assert.Equal(0, result.BonusesEarned);
        Assert.Null(result.CustomerName);

        // Assert: verify corresponding receipt line item was written to PURCHASE_ITEM.
        OperationResult<IReadOnlyList<PurchaseItem>> items =
            await db.PurchaseItems.GetByPurchaseIdAsync(result.PurchaseId);
        Assert.True(items.IsSuccess);
        PurchaseItem line = Assert.Single(items.Value!);
        Assert.Equal(db.Espresso.Id, line.DrinkId);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(2.50m, line.UnitPrice);
    }

    // Tests end-to-end loyalty-card checkout: awards bonus points, updates customer balance, and increments USER_DRINK.
    [Fact]
    public async Task PurchaseService_CardCheckout_UpdatesBonusBalanceAndDrinkCounter()
    {
        // Arrange: prepare a cart with 2 Lattes (2 * 4.00 = 8.00) linked to the seeded customer's loyalty card.
        await using PurchaseTestDb db = await PurchaseTestDb.CreateAsync(UserRole.Cashier);

        PurchaseRequest request = new(
            employeeId: db.EmployeeId,
            items: [new CartItem(db.Latte, 2)],
            bonusesToSpend: 0,
            cardId: db.CardId);

        // Act: process checkout with the active loyalty card.
        PurchaseResult result = await db.Service.ProcessPurchaseAsync(request);

        // Assert: verify receipt result includes customer display name, total amount, and earned bonuses.
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("Test Customer", result.CustomerName);
        Assert.Equal(8.00m, result.TotalAmount);
        Assert.True(result.BonusesEarned > 0);
        Assert.Equal(db.InitialBonus + result.BonusesEarned, result.UpdatedBonusBalance);

        // Assert: verify the customer's persisted bonus balance in CUSTOMERS matches the receipt summary.
        Customer? customer = await db.Customers.GetByIdAsync(db.CustomerId);
        Assert.NotNull(customer);
        Assert.Equal(result.UpdatedBonusBalance, customer!.BonusBalance);

        // Assert: verify USER_DRINK counter recorded 2 units of Latte for this customer.
        OperationResult<IReadOnlyList<UserDrink>> counters =
            await db.UserDrinks.GetByCustomerIdAsync(db.CustomerId);
        Assert.True(counters.IsSuccess);
        UserDrink counter = Assert.Single(counters.Value!);
        Assert.Equal(db.Latte.Id, counter.DrinkId);
        Assert.Equal(2, counter.Quantity);
    }

    // Tests transaction atomicity: if saving line items fails mid-checkout, the purchase header and bonus balance roll back.
    [Fact]
    public async Task PurchaseService_WhenItemInsertFails_RollsBackEntireTransaction()
    {
        // Arrange: attach a SQLite trigger that aborts any INSERT into PURCHASE_ITEM.
        await using PurchaseTestDb db = await PurchaseTestDb.CreateAsync(UserRole.Cashier);
        db.ExecuteSql("CREATE TRIGGER fail_item_insert BEFORE INSERT ON PURCHASE_ITEM BEGIN SELECT RAISE(ABORT, 'forced failure'); END;");

        PurchaseRequest request = new(
            employeeId: db.EmployeeId,
            items: [new CartItem(db.Espresso, 1)],
            cardId: db.CardId);

        // Act: attempt checkout while line-item insertion is rigged to fail.
        PurchaseResult result = await db.Service.ProcessPurchaseAsync(request);

        // Assert: verify checkout reports failure.
        Assert.False(result.IsSuccess);

        // Assert: verify the transaction rolled back and no orphaned header exists in PURCHASES.
        OperationResult<IReadOnlyList<Purchase>> purchases = await db.Purchases.GetAllAsync();
        Assert.True(purchases.IsSuccess);
        Assert.Empty(purchases.Value!);

        // Assert: verify the customer's bonus balance remained unchanged at its initial value.
        Customer? customer = await db.Customers.GetByIdAsync(db.CustomerId);
        Assert.NotNull(customer);
        Assert.Equal(db.InitialBonus, customer!.BonusBalance);
    }

    // Tests access control in PurchaseService when no employee session is signed in.
    [Fact]
    public async Task PurchaseService_Unauthenticated_DeniesCheckout()
    {
        // Arrange: create test environment with role = null (logged-out session).
        await using PurchaseTestDb db = await PurchaseTestDb.CreateAsync(null);

        PurchaseRequest request = new(
            employeeId: db.EmployeeId,
            items: [new CartItem(db.Espresso, 1)]);

        // Act: attempt to process a purchase without an active employee session.
        PurchaseResult result = await db.Service.ProcessPurchaseAsync(request);

        // Assert: verify checkout is rejected with UNAUTHENTICATED error code.
        Assert.False(result.IsSuccess);
        Assert.Equal("UNAUTHENTICATED", result.ErrorCode);
    }

    // Temporary file-backed SQLite fixture wiring all repositories, session state, and PurchaseService.
    private sealed class PurchaseTestDb : IAsyncDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnectionFactory _factory;

        public PurchaseRepository Purchases { get; }
        public PurchaseItemRepository PurchaseItems { get; }
        public UserDrinkRepository UserDrinks { get; }
        public CustomerRepository Customers { get; }
        public IPurchaseService Service { get; }

        public Drink Espresso { get; }
        public Drink Latte { get; }
        public int CustomerId { get; } = 10;
        public int CardId { get; } = 100;
        public int EmployeeId { get; } = 7;
        public int InitialBonus { get; } = 30;

        private PurchaseTestDb(
            string dbPath,
            SqliteConnectionFactory factory,
            PurchaseRepository purchases,
            PurchaseItemRepository purchaseItems,
            UserDrinkRepository userDrinks,
            CustomerRepository customers,
            IPurchaseService service,
            Drink espresso,
            Drink latte)
        {
            _dbPath = dbPath;
            _factory = factory;
            Purchases = purchases;
            PurchaseItems = purchaseItems;
            UserDrinks = userDrinks;
            Customers = customers;
            Service = service;
            Espresso = espresso;
            Latte = latte;
        }

        // Creates a fresh SQLite database file, runs all DDL schemas, seeds test entities, and builds PurchaseService.
        public static async Task<PurchaseTestDb> CreateAsync(UserRole? role)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                $"coffeeloyalty_purchases_{Guid.NewGuid():N}.db");

            SqliteConnectionFactory factory = new(path);

            await using (SqliteConnection connection = factory.OpenConnection())
            {
                // Execute all embedded SQL table schemas in foreign-key dependency order.
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

                // Seed baseline category, cashier employee, two active drinks, a customer with 30 bonuses, and an active card.
                await using SqliteCommand seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO CATEGORIES (categoryID, categoryName, bonusAccrualRate, upgradeThreshold)
                    VALUES (1, 'General', 5.0, 0);
                    INSERT INTO EMPLOYEES (employeeID, employeeName, employeeRole, employeePassword, isActive)
                    VALUES (7, 'Barista', 'Cashier', 'unused-hash', 1);
                    INSERT INTO DRINKS (drinkID, drinkName, drinkPrice, drinkType, isActive)
                    VALUES
                        (1, 'Espresso', 2.50, 'Coffee', 1),
                        (2, 'Latte', 4.00, 'Coffee', 1);
                    INSERT INTO CUSTOMERS (customerID, categoryID, customerName, bonusBalance, isActive, timeStamp)
                    VALUES (10, 1, 'Test Customer', 30, 1, '2026-01-01T00:00:00Z');
                    INSERT INTO CARDS (cardID, customerID, isActive)
                    VALUES (100, 10, 1);
                    """;
                await seed.ExecuteNonQueryAsync();
            }

            // Wire real SQLite repositories and domain services around the temporary database.
            PurchaseRepository purchases = new(factory);
            PurchaseItemRepository purchaseItems = new(factory);
            UserDrinkRepository userDrinks = new(factory);
            DrinkRepository drinks = new(factory);
            CardRepository cards = new(factory);
            CustomerRepository customers = new(factory);
            LogRepository logs = new(factory);

            EmployeeSession session = new();
            if (role.HasValue)
            {
                session.SetCurrentEmployee(new Employee("Barista", role.Value) { Id = 7 });
            }

            LogService logService = new(logs, session);
            AuthService authService = new(new EmployeeRepository(factory), logService, session);
            BonusService bonusService = new();

            IPurchaseService service = new PurchaseService(
                purchases,
                purchaseItems,
                userDrinks,
                drinks,
                cards,
                customers,
                bonusService,
                authService,
                logService,
                factory);

            Drink espresso = new("Espresso", 2.50m, "Coffee", true, null) { Id = 1 };
            Drink latte = new("Latte", 4.00m, "Coffee", true, null) { Id = 2 };

            return new PurchaseTestDb(
                path,
                factory,
                purchases,
                purchaseItems,
                userDrinks,
                customers,
                service,
                espresso,
                latte);
        }

        // Executes raw SQL statements (such as test triggers) directly against the temporary database.
        public void ExecuteSql(string sql)
        {
            using SqliteConnection connection = _factory.OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        // Releases SQLite connection pools and deletes the temporary database file after each test.
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