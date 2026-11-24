using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.Interfaces.Services;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.Repositories;
using CoffeeLoyalty.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoffeeLoyalty.Core.Tests;

public class CustomerServiceTests
{
    [Theory]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Manager)]
    public async Task Registration_CreatesCustomerAndCardAndAuditEntries(UserRole role)
    {
        await using TestDatabase db = new(role);
        OperationResult<LoyaltyCard> result = await db.Service.RegisterCustomerAsync(" Anna ");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        LoyaltyCard card = Assert.IsType<LoyaltyCard>(result.Value);
        Customer saved = Assert.Single(await db.Customers.GetAllAsync());
        Assert.Equal("Anna", saved.CustomerName);
        Assert.Equal(db.BasicId, saved.CategoryId);
        Assert.Equal(0, saved.BonusBalance);
        Assert.True(saved.IsActive);
        Assert.True(card.IsActive);
        Assert.Equal(saved.Id, card.CustomerId);
        Assert.Equal(card.Id, Assert.Single(await db.Cards.GetAllAsync()).Id);
        IReadOnlyList<LogEntry> logs = await db.Logs.GetAllAsync();
        Assert.Contains(logs, l => l.ActionType == "LogCustomerRegistered" && l.EmployeeId == db.EmployeeId && l.ObjectId == saved.Id);
        Assert.Contains(logs, l => l.ActionType == "LogCardIssued" && l.EmployeeId == db.EmployeeId && l.ObjectId == card.Id);
    }

    [Fact]
    public async Task Registration_CardInsertFails_RollsBackCustomerAndInputId()
    {
        await using TestDatabase db = new(UserRole.Cashier);
        db.Sql("CREATE TRIGGER fail_card BEFORE INSERT ON CARDS BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        Customer customer = new(db.BasicId, "Rollback");
        OperationResult<LoyaltyCard> result = await db.Service.RegisterCustomerAsync(customer);
        Assert.False(result.IsSuccess);
        Assert.Equal(0, customer.Id);
        Assert.Empty(await db.Customers.GetAllAsync());
        Assert.Empty(await db.Cards.GetAllAsync());
        Assert.Contains(await db.Logs.GetAllAsync(), l => l.ActionType == "ErrorCustomerOperation" && l.EmployeeId == db.EmployeeId);
    }

    [Fact]
    public async Task Registration_UsesGeneralWhenBasicIsAbsent()
    {
        await using TestDatabase db = new(UserRole.Cashier);
        db.Sql("UPDATE CATEGORIES SET categoryName = 'General' WHERE categoryName = 'Basic';");
        Assert.True((await db.Service.RegisterCustomerAsync("General customer")).IsSuccess);
        Assert.Equal(db.BasicId, Assert.Single(await db.Customers.GetAllAsync()).CategoryId);
    }

    [Fact]
    public async Task Registration_MissingInitialCategory_ReturnsFailure()
    {
        await using TestDatabase db = new(UserRole.Cashier);
        db.Sql("DELETE FROM CATEGORIES WHERE categoryName = 'Basic';");
        Assert.Equal("CATEGORY_NOT_FOUND", (await db.Service.RegisterCustomerAsync("No category")).ErrorCode);
        Assert.Empty(await db.Customers.GetAllAsync());
    }

    [Fact]
    public async Task Registration_CashierCannotSelectHigherTierOrStartingBonus()
    {
        await using TestDatabase db = new(UserRole.Cashier);
        Assert.Equal("ACCESS_DENIED", (await db.Service.RegisterCustomerAsync(new Customer(db.SilverId, "Silver"))).ErrorCode);
        Assert.Equal("INVALID_INPUT", (await db.Service.RegisterCustomerAsync(new Customer(db.BasicId, "Bonus", 99))).ErrorCode);
        Assert.Empty(await db.Customers.GetAllAsync());
    }

    [Fact]
    public async Task Registration_ManagerCanSelectExistingCategory()
    {
        await using TestDatabase db = new(UserRole.Manager);
        Customer customer = new(db.SilverId, "Silver customer");
        Assert.True((await db.Service.RegisterCustomerAsync(customer)).IsSuccess);
        Assert.True(customer.Id > 0);
        Assert.Equal(db.SilverId, Assert.Single(await db.Customers.GetAllAsync()).CategoryId);
    }

    [Fact]
    public async Task Registration_InvalidInputsAreRejected()
    {
        await using TestDatabase db = new(UserRole.Manager);
        Assert.Equal("INVALID_INPUT", (await db.Service.RegisterCustomerAsync("   ")).ErrorCode);
        Assert.Equal("INVALID_INPUT", (await db.Service.RegisterCustomerAsync("Future", DateOnly.FromDateTime(DateTime.Today.AddDays(1)))).ErrorCode);
        Assert.Equal("CATEGORY_NOT_FOUND", (await db.Service.RegisterCustomerAsync(new Customer(99999, "Missing"))).ErrorCode);
        Assert.Empty(await db.Customers.GetAllAsync());
    }

    [Fact]
    public async Task ReadOperations_ReturnAllActiveSearchAndCardOwner()
    {
        await using TestDatabase db = new(UserRole.Cashier);
        Customer active = await db.AddCustomerAsync("Anna Peterson");
        Customer blocked = await db.AddCustomerAsync("Mark", false);
        LoyaltyCard card = new(active.Id, false);
        await db.Cards.AddAsync(card);
        Assert.Equal(2, (await db.Service.GetAllCustomersAsync()).Value!.Count);
        Assert.Equal(active.Id, Assert.Single((await db.Service.GetActiveCustomersAsync()).Value!).Id);
        Assert.Equal(active.Id, Assert.Single((await db.Service.SearchCustomersAsync(" aNNa ")).Value!).Id);
        Assert.Equal(blocked.Id, (await db.Service.GetCustomerByIdAsync(blocked.Id)).Value!.Id);
        Assert.Equal(active.Id, (await db.Service.GetCustomerByCardAsync(card.Id)).Value!.Id);
        Assert.Equal(card.Id, Assert.Single((await db.Service.GetCustomerCardsAsync(active.Id)).Value!).Id);
    }

    [Fact]
    public async Task ReadOperations_InvalidAndMissingIdsReturnStructuredErrors()
    {
        await using TestDatabase db = new(UserRole.Cashier);
        Assert.Equal("INVALID_INPUT", (await db.Service.GetCustomerByIdAsync(0)).ErrorCode);
        Assert.Equal("NOT_FOUND", (await db.Service.GetCustomerByIdAsync(999)).ErrorCode);
        Assert.Equal("NOT_FOUND", (await db.Service.GetCustomerByCardAsync(999)).ErrorCode);
        Assert.Equal("NOT_FOUND", (await db.Service.GetCustomerCardsAsync(999)).ErrorCode);
        Assert.Equal("INVALID_INPUT", (await db.Service.SearchCustomersAsync(" ")).ErrorCode);
        Assert.Empty((await db.Service.SearchCustomersAsync("No match")).Value!);
    }

    [Fact]
    public async Task Cashier_CannotPerformAnyManagerOperation()
    {
        await using TestDatabase db = new(UserRole.Cashier);
        Customer customer = await db.AddCustomerAsync("Original");
        LoyaltyCard card = new(customer.Id);
        await db.Cards.AddAsync(card);
        customer.CustomerName = "Forbidden";
        OperationResult[] results = [
            await db.Service.UpdateCustomerAsync(customer),
            await db.Service.ChangeCategoryAsync(customer.Id, db.SilverId),
            await db.Service.IssueCardAsync(customer.Id),
            await db.Service.SetCustomerActiveStatusAsync(customer.Id, false),
            await db.Service.SetCardActiveStatusAsync(card.Id, false)];
        Assert.All(results, r => Assert.Equal("ACCESS_DENIED", r.ErrorCode));
        Customer saved = Assert.Single(await db.Customers.GetAllAsync());
        Assert.Equal("Original", saved.CustomerName);
        Assert.Equal(db.BasicId, saved.CategoryId);
        Assert.True(saved.IsActive);
        Assert.True(Assert.Single(await db.Cards.GetAllAsync()).IsActive);
        Assert.Equal(5, (await db.Logs.GetAllAsync()).Count(l => l.ActionType == "ErrorCustomerAccessDenied" && l.EmployeeId == db.EmployeeId));
    }

    [Fact]
    public async Task LoggedOut_CannotReadOrRegister()
    {
        await using TestDatabase db = new(null);
        Assert.Equal("ACCESS_DENIED", (await db.Service.GetAllCustomersAsync()).ErrorCode);
        Assert.Equal("ACCESS_DENIED", (await db.Service.RegisterCustomerAsync("Denied")).ErrorCode);
        Assert.Empty(await db.Customers.GetAllAsync());
        Assert.All(await db.Logs.GetAllAsync(), l => Assert.Null(l.EmployeeId));
    }

    [Fact]
    public async Task UpdateProfile_PreservesCurrentBalanceCategoryStatusAndTimestamp()
    {
        await using TestDatabase db = new(UserRole.Manager);
        Customer original = await db.AddCustomerAsync("Original", false, 35);
        Customer edited = new(db.SilverId, "Updated", 0, new DateOnly(2000, 1, 1)) { Id = original.Id };
        Assert.True((await db.Service.UpdateCustomerAsync(edited)).IsSuccess);
        Customer saved = Assert.Single(await db.Customers.GetAllAsync());
        Assert.Equal("Updated", saved.CustomerName);
        Assert.Equal(new DateOnly(2000, 1, 1), saved.BirthDate);
        Assert.Equal(original.CategoryId, saved.CategoryId);
        Assert.Equal(original.BonusBalance, saved.BonusBalance);
        Assert.Equal(original.TimeStamp, saved.TimeStamp);
        Assert.False(saved.IsActive);
        Assert.Contains(await db.Logs.GetAllAsync(), l => l.ActionType == "LogCustomerUpdated" && l.ObjectId == original.Id);
    }

    [Fact]
    public async Task ChangeCategory_PreservesOtherFieldsAndRejectsMissingCategory()
    {
        await using TestDatabase db = new(UserRole.Manager);
        Customer customer = await db.AddCustomerAsync("Tier", true, 20);
        Assert.Equal("CATEGORY_NOT_FOUND", (await db.Service.ChangeCategoryAsync(customer.Id, 999)).ErrorCode);
        Assert.True((await db.Service.ChangeCategoryAsync(customer.Id, db.SilverId)).IsSuccess);
        Customer saved = Assert.Single(await db.Customers.GetAllAsync());
        Assert.Equal(db.SilverId, saved.CategoryId);
        Assert.Equal(20, saved.BonusBalance);
        Assert.Equal("Tier", saved.CustomerName);
    }

    [Fact]
    public async Task Manager_CanBlockAndUnblockCustomerAndCardIndependently()
    {
        await using TestDatabase db = new(UserRole.Manager);
        Customer customer = await db.AddCustomerAsync("Status");
        LoyaltyCard card = new(customer.Id);
        await db.Cards.AddAsync(card);
        Assert.True((await db.Service.SetCardActiveStatusAsync(card.Id, false)).IsSuccess);
        Assert.True((await db.Service.SetCustomerActiveStatusAsync(customer.Id, false)).IsSuccess);
        Assert.False(Assert.Single(await db.Customers.GetAllAsync()).IsActive);
        Assert.True((await db.Service.SetCustomerActiveStatusAsync(customer.Id, true)).IsSuccess);
        Assert.False(Assert.Single(await db.Cards.GetAllAsync()).IsActive);
        Assert.True((await db.Service.SetCardActiveStatusAsync(card.Id, true)).IsSuccess);
        Assert.True(Assert.Single(await db.Cards.GetAllAsync()).IsActive);
        Assert.Equal(4, (await db.Logs.GetAllAsync()).Count);
    }

    [Fact]
    public async Task IssueCard_RequiresActiveCustomerAndGeneratesUniqueIds()
    {
        await using TestDatabase db = new(UserRole.Manager);
        Customer customer = await db.AddCustomerAsync("Active");
        Customer inactive = await db.AddCustomerAsync("Inactive", false);
        LoyaltyCard first = (await db.Service.IssueCardAsync(customer.Id)).Value!;
        LoyaltyCard second = (await db.Service.IssueCardAsync(customer.Id)).Value!;
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(customer.Id, second.CustomerId);
        Assert.Equal("CUSTOMER_INACTIVE", (await db.Service.IssueCardAsync(inactive.Id)).ErrorCode);
        Assert.Equal("NOT_FOUND", (await db.Service.IssueCardAsync(999)).ErrorCode);
        Assert.Equal(2, (await db.Cards.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Cancellation_PropagatesAndDoesNotWrite()
    {
        await using TestDatabase db = new(UserRole.Manager);
        using CancellationTokenSource source = new();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Service.RegisterCustomerAsync("Cancelled", cancellationToken: source.Token));
        Assert.Empty(await db.Customers.GetAllAsync());
        Assert.Empty(await db.Cards.GetAllAsync());
    }

    [Fact]
    public async Task AuditFailure_DoesNotMisreportCommittedRegistration()
    {
        await using TestDatabase db = new(UserRole.Cashier);
        db.Sql("CREATE TRIGGER fail_audit BEFORE INSERT ON LOGS BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        Assert.True((await db.Service.RegisterCustomerAsync("Committed")).IsSuccess);
        Assert.Single(await db.Customers.GetAllAsync());
        Assert.Single(await db.Cards.GetAllAsync());
    }

    // Uses real SQLite schemas, repositories, AuthService, EmployeeSession and LogService.
    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"coffeeloyalty_service_{Guid.NewGuid():N}.db");
        private readonly SqliteConnectionFactory _factory;
        public CustomerRepository Customers { get; }
        public CardRepository Cards { get; }
        public LogRepository Logs { get; }
        public ICustomerService Service { get; }
        public int BasicId { get; }
        public int SilverId { get; }
        public int EmployeeId { get; }

        public TestDatabase(UserRole? role)
        {
            _factory = new SqliteConnectionFactory(_path);
            using (SqliteConnection connection = _factory.OpenConnection())
            {
                foreach (string name in new[] { "Categories", "Customers", "Cards", "Employees", "Logs" })
                {
                    using Stream stream = typeof(DatabaseInitializer).Assembly.GetManifestResourceStream($"CoffeeLoyalty.Core.data.schemas.{name}Schema.sql")!;
                    using StreamReader reader = new(stream);
                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText = reader.ReadToEnd();
                    command.ExecuteNonQuery();
                }
            }
            // Use non-default IDs to prove that no category ID is hardcoded.
            Sql("INSERT INTO CATEGORIES (categoryID, categoryName, bonusAccrualRate, upgradeThreshold) VALUES (10, 'Basic', 0, 0), (20, 'Silver', 0, 50);");
            BasicId = 10;
            SilverId = 20;
            EmployeeId = 7;
            Sql($"INSERT INTO EMPLOYEES (employeeID, employeeName, employeeRole, employeePassword, isActive) VALUES (7, 'Test', '{role ?? UserRole.Cashier}', 'unused-test-hash', 1);");
            Customers = new CustomerRepository(_factory);
            Cards = new CardRepository(_factory);
            Logs = new LogRepository(_factory);
            EmployeeSession session = new();
            if (role.HasValue) session.SetCurrentEmployee(new Employee("Test", role.Value) { Id = EmployeeId });
            LogService logService = new(Logs, session);
            AuthService auth = new(new EmployeeRepository(_factory), logService, session);
            Service = new CustomerService(Customers, Cards, new CategoryRepository(_factory), auth, logService, _factory);
        }

        public void Sql(string sql)
        {
            using SqliteConnection connection = _factory.OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public async Task<Customer> AddCustomerAsync(string name, bool active = true, int balance = 0)
        {
            Customer customer = new(BasicId, name, balance, isActive: active);
            await Customers.AddAsync(customer);
            return customer;
        }

        public ValueTask DisposeAsync()
        {
            TestDatabasePool.Clear(_path);
            File.Delete(_path);
            return ValueTask.CompletedTask;
        }
    }
}
