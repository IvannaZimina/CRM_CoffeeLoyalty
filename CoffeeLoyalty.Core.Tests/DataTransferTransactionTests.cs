using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.interfaces.services;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.Repositories;
using CoffeeLoyalty.Core.Services;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

namespace CoffeeLoyalty.Core.Tests;

public class DataTransferTransactionTests
{
    [Fact]
    public async Task ValidImport_CommitsAllRecordsAndRemapsCardOwner()
    {
        await using TestDatabase db = new();
        var before = db.Counts();
        await db.WritePayloadAsync();

        OperationResult<int> result = await db.CreateService().ImportFromJsonAsync(db.JsonPath);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(3, result.Value);
        Assert.Equal((before.drinks + 1, before.customers + 1, before.cards + 1), db.Counts());
        Customer imported = Assert.Single(await db.Customers.GetAllAsync(), x => x.CustomerName == "Imported Customer");
        Assert.NotEqual(99999, imported.Id);
        Assert.Equal(15, imported.BonusBalance);
        LoyaltyCard card = Assert.Single(await db.Cards.GetByCustomerIdAsync(imported.Id));
        Assert.True(card.IsActive);
        LogEntry log = Assert.Single(await db.Logs.GetAllAsync(), x => x.ActionType == "LogImportJson");
        Assert.Equal(db.ManagerId, log.EmployeeId);
        Assert.Null(log.ObjectId);
    }

    [Fact]
    public async Task CardInsertFailure_RollsBackDrinksCustomersAndCards()
    {
        await using TestDatabase db = new();
        var before = db.Counts();
        // Force the second card insert to fail after a drink, customer and first card have been inserted.
        db.Execute("""
            CREATE TRIGGER reject_second_import_card BEFORE INSERT ON CARDS
            WHEN NEW.isActive = 0
            BEGIN SELECT RAISE(ABORT, 'forced card failure'); END;
            """);
        DataTransferPayloadDto payload = db.Payload();
        payload.Cards.Add(new CardTransferDto { CustomerId = 99999, IsActive = false });
        await db.WritePayloadAsync(payload);

        OperationResult<int> result = await db.CreateService().ImportFromJsonAsync(db.JsonPath);

        Assert.False(result.IsSuccess);
        Assert.Equal("IMPORT_PERSISTENCE_ERROR", result.ErrorCode);
        Assert.Equal(before, db.Counts());
        Assert.DoesNotContain(await db.Customers.GetAllAsync(), x => x.CustomerName == "Imported Customer");
        Assert.DoesNotContain((await db.Drinks.GetAllAsync()).Value!, x => x.DrinkName == "Imported Drink");
        Assert.Contains(await db.Logs.GetAllAsync(), x => x.ActionType == "ErrorImportJson");
    }

    [Fact]
    public async Task CancellationAfterCardInsert_RollsBackEntireBatch()
    {
        await using TestDatabase db = new();
        var before = db.Counts();
        await db.WritePayloadAsync();
        using CancellationTokenSource cancellation = new();
        ICardRepository cancellingCards = new CancelAfterInsertCards(db.Cards, cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            db.CreateService(cancellingCards).ImportFromJsonAsync(db.JsonPath, cancellation.Token));

        Assert.Equal(before, db.Counts());
        Assert.Contains(await db.Logs.GetAllAsync(), x => x.ActionType == "ErrorImportCancelled");
        Assert.DoesNotContain(await db.Logs.GetAllAsync(), x => x.ActionType == "LogImportJson");
    }

    [Fact]
    public async Task InvalidCategoryForeignKey_RollsBackEarlierDrinkInsert()
    {
        await using TestDatabase db = new();
        var before = db.Counts();
        DataTransferPayloadDto payload = db.Payload();
        payload.Customers[0].CategoryId = 99999;
        await db.WritePayloadAsync(payload);

        OperationResult<int> result = await db.CreateService().ImportFromJsonAsync(db.JsonPath);

        Assert.False(result.IsSuccess);
        Assert.Equal(before, db.Counts());
    }

    [Fact]
    public async Task AuditExceptionAfterCommit_DoesNotReportImportFailure()
    {
        await using TestDatabase db = new();
        var before = db.Counts();
        await db.WritePayloadAsync();

        OperationResult<int> result = await db.CreateService(logService: new ThrowingInfoLog(db.LogService))
            .ImportFromJsonAsync(db.JsonPath);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal((before.drinks + 1, before.customers + 1, before.cards + 1), db.Counts());
    }

    [Fact]
    public async Task DrinkRepository_RejectsInvalidExternalTransaction()
    {
        await using TestDatabase db = new();
        var before = db.Counts();
        OperationResult<Drink> result = await db.Drinks.AddAsync(new Drink("New drink", 2m, "Coffee"), new object());
        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_TRANSACTION", result.ErrorCode);
        Assert.Equal(before, db.Counts());
    }

    [Fact]
    public async Task ImportWithNullRecord_ReturnsValidationFailureWithoutWrites()
    {
        await using TestDatabase db = new();
        var before = db.Counts();
        DataTransferPayloadDto payload = db.Payload();
        payload.Drinks.Add(null!);
        await db.WritePayloadAsync(payload);
        OperationResult<int> result = await db.CreateService().ImportFromJsonAsync(db.JsonPath);
        Assert.False(result.IsSuccess);
        Assert.Equal("VALIDATION_FAILED", result.ErrorCode);
        Assert.Equal(before, db.Counts());
    }

    // Uses real repositories and an isolated SQLite file; never touches the application database.
    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"coffeeloyalty_import_{Guid.NewGuid():N}.db");
        public string JsonPath { get; }
        public SqliteConnectionFactory Factory { get; }
        public DrinkRepository Drinks { get; }
        public CustomerRepository Customers { get; }
        public CardRepository Cards { get; }
        public LogRepository Logs { get; }
        public LogService LogService { get; }
        public AuthService Auth { get; }
        public int ManagerId { get; }
        private readonly int _categoryId;

        public TestDatabase()
        {
            JsonPath = Path.ChangeExtension(_path, ".json");
            Factory = new(_path);
            new DatabaseInitializer(Factory, new SeedDataFactory()).Initialize();
            _categoryId = Scalar("SELECT categoryID FROM CATEGORIES WHERE categoryName = 'Basic';");
            ManagerId = Scalar("SELECT employeeID FROM EMPLOYEES WHERE employeeName = 'manager';");
            Drinks = new(Factory);
            Customers = new(Factory);
            Cards = new(Factory);
            Logs = new(Factory);
            EmployeeSession session = new();
            session.SetCurrentEmployee(new Employee("manager", UserRole.Manager) { Id = ManagerId });
            LogService = new(Logs, session);
            Auth = new(new EmployeeRepository(Factory), LogService, session);
        }

        public DataTransferService CreateService(ICardRepository? cards = null, ILogService? logService = null) =>
            new(Drinks, Customers, cards ?? Cards, new PurchaseRepository(Factory), Logs, Auth,
                logService ?? LogService, Factory);

        public DataTransferPayloadDto Payload() => new()
        {
            Version = "1.0",
            Drinks = [new DrinkTransferDto { DrinkName = "Imported Drink", DrinkPrice = 3m, DrinkType = "Coffee" }],
            Customers = [new CustomerTransferDto { Id = 99999, CategoryId = _categoryId, CustomerName = "Imported Customer", BonusBalance = 15 }],
            Cards = [new CardTransferDto { CustomerId = 99999, IsActive = true }]
        };

        public Task WritePayloadAsync(DataTransferPayloadDto? payload = null) =>
            File.WriteAllTextAsync(JsonPath, JsonSerializer.Serialize(payload ?? Payload()));

        public (int drinks, int customers, int cards) Counts() =>
            (Scalar("SELECT COUNT(*) FROM DRINKS;"), Scalar("SELECT COUNT(*) FROM CUSTOMERS;"), Scalar("SELECT COUNT(*) FROM CARDS;"));

        private int Scalar(string sql)
        {
            using SqliteConnection connection = Factory.OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(command.ExecuteScalar());
        }

        public void Execute(string sql)
        {
            using SqliteConnection connection = Factory.OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public ValueTask DisposeAsync()
        {
            TestDatabasePool.Clear(_path);
            if (File.Exists(JsonPath)) File.Delete(JsonPath);
            if (File.Exists(_path)) File.Delete(_path);
            return ValueTask.CompletedTask;
        }
    }

    // Cancels deterministically after a real transactional card insert, before the service commits.
    private sealed class CancelAfterInsertCards(ICardRepository inner, CancellationTokenSource source) : ICardRepository
    {
        public Task<IReadOnlyList<LoyaltyCard>> GetAllAsync(CancellationToken cancellationToken = default) => inner.GetAllAsync(cancellationToken);
        public Task<LoyaltyCard?> GetByIdAsync(int cardId, CancellationToken cancellationToken = default) => inner.GetByIdAsync(cardId, cancellationToken);
        public Task<IReadOnlyList<LoyaltyCard>> GetByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default) => inner.GetByCustomerIdAsync(customerId, cancellationToken);
        public Task<int> AddAsync(LoyaltyCard card, CancellationToken cancellationToken = default) => inner.AddAsync(card, cancellationToken);
        public async Task<int> AddAsync(LoyaltyCard card, object? transaction, CancellationToken cancellationToken = default)
        {
            int id = await inner.AddAsync(card, transaction, cancellationToken);
            source.Cancel();
            return id;
        }
        public Task<bool> SetActiveStatusAsync(int cardId, bool isActive, CancellationToken cancellationToken = default) => inner.SetActiveStatusAsync(cardId, isActive, cancellationToken);
    }

    // Simulates an audit sink failure only on informational events after commit.
    private sealed class ThrowingInfoLog(ILogService inner) : ILogService
    {
        public Task<OperationResult> LogInfoAsync(string objectType, LogActionType actionType, string description,
            int? objectId = null, CancellationToken cancellationToken = default) => throw new IOException("Audit unavailable");
        public Task<OperationResult> LogErrorAsync(string objectType, LogActionType actionType, string description,
            int? objectId = null, CancellationToken cancellationToken = default) => inner.LogErrorAsync(objectType, actionType, description, objectId, cancellationToken);
        public Task<OperationResult<IReadOnlyList<LogEntry>>> GetAllAsync(CancellationToken cancellationToken = default) => inner.GetAllAsync(cancellationToken);
        public Task<OperationResult<IReadOnlyList<LogEntry>>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default) => inner.GetByDateRangeAsync(startDate, endDate, cancellationToken);
        public Task<OperationResult<IReadOnlyList<LogEntry>>> GetByActionTypeAsync(LogActionType actionType, CancellationToken cancellationToken = default) => inner.GetByActionTypeAsync(actionType, cancellationToken);
    }
}
