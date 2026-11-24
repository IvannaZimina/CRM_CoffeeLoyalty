using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.interfaces.services;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.Repositories;
using CoffeeLoyalty.Core.Services;
using CoffeeLoyalty.Core.Validation;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace CoffeeLoyalty.Core.Tests;

public class AuthServiceTests
{
    [Fact]
    public async Task Manager_ValidPassword_SignsInAndGrantsPermissions()
    {
        Employee manager = MakeEmployee(2, "manager", UserRole.Manager, PasswordHasher.HashPassword("correct"));
        FakeEmployees employees = new(manager);
        EmployeeSession session = new();
        FakeLogs logs = new();
        AuthService auth = new(employees, logs, session);

        OperationResult result = await auth.LoginAsync("manager", UserRole.Manager, "correct");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, session.EmployeeId);
        Assert.True(auth.HasPermission(UserRole.Manager));
        Assert.True(auth.HasPermission(UserRole.Cashier));
        Assert.Contains(logs.Events, x => x.action == "LogLogin");
    }

    [Theory]
    [InlineData("incorrect")]
    [InlineData(null)]
    public async Task Manager_InvalidPassword_DeniedWithoutSession(string? password)
    {
        Employee manager = MakeEmployee(2, "manager", UserRole.Manager, PasswordHasher.HashPassword("correct"));
        EmployeeSession session = new();
        FakeLogs logs = new();
        AuthService auth = new(new FakeEmployees(manager), logs, session);

        OperationResult result = await auth.LoginAsync("manager", UserRole.Manager, password);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_CREDENTIALS", result.ErrorCode);
        Assert.False(session.IsAuthenticated);
        Assert.Contains(logs.Events, x => x.action == "ErrorLogin");
        Assert.All(logs.Events, e => Assert.DoesNotContain("incorrect", e.description));
    }

    [Fact]
    public async Task Cashier_CanSignInWithoutPassword_ButCannotManage()
    {
        Employee cashier = MakeEmployee(3, "cashier", UserRole.Cashier, "test-hash");
        AuthService auth = new(new FakeEmployees(cashier), new FakeLogs(), new EmployeeSession());
        Assert.True((await auth.LoginAsync("cashier", UserRole.Cashier)).IsSuccess);
        Assert.True(auth.HasPermission(UserRole.Cashier));
        Assert.False(auth.HasPermission(UserRole.Manager));
    }

    [Fact]
    public async Task IncorrectRoleOrInactiveEmployee_Denied()
    {
        Employee manager = MakeEmployee(2, "manager", UserRole.Manager, PasswordHasher.HashPassword("correct"));
        Employee cashier = MakeEmployee(3, "cashier", UserRole.Cashier, "test-hash");
        cashier.IsActive = false;
        AuthService auth = new(new FakeEmployees(manager, cashier), new FakeLogs(), new EmployeeSession());

        Assert.False((await auth.LoginAsync("manager", UserRole.Cashier)).IsSuccess);
        Assert.False((await auth.LoginAsync("cashier", UserRole.Cashier)).IsSuccess);
        Assert.False(auth.IsAuthenticated);
    }

    [Fact]
    public async Task FailedUserSwitch_ClearsPreviousManagerAccess()
    {
        Employee manager = MakeEmployee(2, "manager", UserRole.Manager, PasswordHasher.HashPassword("correct"));
        EmployeeSession session = new();
        AuthService auth = new(new FakeEmployees(manager), new FakeLogs(), session);

        Assert.True((await auth.LoginAsync("manager", UserRole.Manager, "correct")).IsSuccess);
        Assert.False((await auth.LoginAsync("missing", UserRole.Cashier)).IsSuccess);
        Assert.False(auth.HasPermission(UserRole.Manager));
        Assert.Null(session.EmployeeId);
    }

    [Fact]
    public async Task Logout_LogsOldActorThenClearsSession()
    {
        Employee manager = MakeEmployee(2, "manager", UserRole.Manager, PasswordHasher.HashPassword("correct"));
        EmployeeSession session = new();
        FakeLogs logs = new();
        AuthService auth = new(new FakeEmployees(manager), logs, session);
        await auth.LoginAsync("manager", UserRole.Manager, "correct");

        Assert.True((await auth.LogoutAsync()).IsSuccess);
        Assert.False(auth.IsAuthenticated);
        Assert.False(auth.HasPermission(UserRole.Manager));
        Assert.Contains(logs.Events, e => e.action == "LogLogout" && e.objectId == 2);
    }

    [Fact]
    public async Task LegacyManagerLogin_UpgradesOldHashToPbkdf2()
    {
        string plainPassword = "ChangeMe!Manager123";
        string legacy = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"manager:{plainPassword}")));
        Employee manager = MakeEmployee(2, "manager", UserRole.Manager, legacy);
        FakeEmployees employees = new(manager);
        AuthService auth = new(employees, new FakeLogs(), new EmployeeSession());

        Assert.True((await auth.LoginAsync("manager", UserRole.Manager, plainPassword)).IsSuccess);
        Assert.True(PasswordHasher.VerifyPassword(plainPassword, manager.EmployeePassword));
        Assert.Equal(1, employees.UpdateCount);
    }

    [Fact]
    public async Task UnrelatedLegacyHash_IsNotAccepted()
    {
        const string password = "legacy-only";
        string legacy = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"alex:{password}")));
        Employee employee = MakeEmployee(7, "alex", UserRole.Manager, legacy);
        AuthService auth = new(new FakeEmployees(employee), new FakeLogs(), new EmployeeSession());
        Assert.False((await auth.LoginAsync("alex", UserRole.Manager, password)).IsSuccess);
    }

    [Fact]
    public async Task SeededManager_UsesModernHashOnFreshDatabase()
    {
        string file = Path.Combine(Path.GetTempPath(), $"coffee_auth68_{Guid.NewGuid():N}.db");
        try
        {
            SqliteConnectionFactory factory = new(file);
            new DatabaseInitializer(factory, new SeedDataFactory()).Initialize();
            EmployeeRepository employees = new(factory);
            Employee? manager = await employees.GetByNameAsync("manager");
            Assert.NotNull(manager);
            Assert.True(PasswordHasher.VerifyPassword("ChangeMe!Manager123", manager.EmployeePassword));

            EmployeeSession session = new();
            LogRepository logRepository = new(factory);
            LogService logs = new(logRepository, session);
            AuthService auth = new(employees, logs, session);
            Assert.True((await auth.LoginAsync("manager", UserRole.Manager, "ChangeMe!Manager123")).IsSuccess);
            Assert.True(session.IsAuthenticated);
            Assert.True((await auth.LogoutAsync()).IsSuccess);
            Assert.False(session.IsAuthenticated);

            LogEntry login = Assert.Single(await logRepository.GetByActionTypeAsync("LogLogin"));
            LogEntry logout = Assert.Single(await logRepository.GetByActionTypeAsync("LogLogout"));
            Assert.Equal(manager.Id, login.EmployeeId);
            Assert.Equal(manager.Id, logout.EmployeeId);
            Assert.Equal(manager.Id, login.ObjectId);
            Assert.Equal(manager.Id, logout.ObjectId);
        }
        finally
        {
            TestDatabasePool.Clear(file);
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public async Task SwitchingToCashier_LogsPreviousLogoutAndRemovesManagerAccess()
    {
        Employee manager = MakeEmployee(2, "manager", UserRole.Manager, PasswordHasher.HashPassword("correct"));
        Employee cashier = MakeEmployee(3, "cashier", UserRole.Cashier, "test-hash");
        EmployeeSession session = new();
        FakeLogs logs = new();
        AuthService auth = new(new FakeEmployees(manager, cashier), logs, session);

        Assert.True((await auth.LoginAsync("manager", UserRole.Manager, "correct")).IsSuccess);
        Assert.True((await auth.LoginAsync("cashier", UserRole.Cashier)).IsSuccess);
        Assert.Equal(3, session.EmployeeId);
        Assert.False(auth.HasPermission(UserRole.Manager));
        Assert.Equal(new[] { "LogLogin", "LogLogout", "LogLogin" }, logs.Events.Select(e => e.action));
        Assert.Equal(2, logs.Events[1].objectId);
    }

    [Fact]
    public async Task LoginCancelledDuringAudit_DoesNotLeaveActiveSession()
    {
        using CancellationTokenSource source = new();
        Employee cashier = MakeEmployee(3, "cashier", UserRole.Cashier, "test-hash");
        EmployeeSession session = new();
        FakeLogs logs = new() { BeforeInfo = () => source.Cancel() };
        AuthService auth = new(new FakeEmployees(cashier), logs, session);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            auth.LoginAsync("cashier", UserRole.Cashier, cancellationToken: source.Token));
        Assert.False(session.IsAuthenticated);
        Assert.False(auth.HasPermission(UserRole.Cashier));
    }

    [Fact]
    public async Task NonSeedLegacyHashForManager_IsNotAccepted()
    {
        const string password = "not-original-seed";
        string legacy = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"manager:{password}")));
        Employee manager = MakeEmployee(2, "manager", UserRole.Manager, legacy);
        AuthService auth = new(new FakeEmployees(manager), new FakeLogs(), new EmployeeSession());

        Assert.False((await auth.LoginAsync("manager", UserRole.Manager, password)).IsSuccess);
        Assert.False(auth.IsAuthenticated);
    }

    private static Employee MakeEmployee(int id, string name, UserRole role, string hash) =>
        new(name, role, hash) { Id = id };

    private sealed class FakeEmployees(params Employee[] employees) : IEmployeeRepository
    {
        private readonly List<Employee> _employees = [.. employees];
        public int UpdateCount { get; private set; }
        public Task<IReadOnlyList<Employee>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Employee>>(_employees);
        public Task<Employee?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_employees.FirstOrDefault(x => x.Id == id));
        public Task<Employee?> GetByNameAsync(string employeeName, CancellationToken cancellationToken = default) =>
            Task.FromResult(_employees.FirstOrDefault(x => x.EmployeeName == employeeName));
        public Task<int> AddAsync(Employee employee, CancellationToken cancellationToken = default)
        {
            _employees.Add(employee);
            return Task.FromResult(employee.Id);
        }
        public Task<bool> UpdateAsync(Employee employee, CancellationToken cancellationToken = default)
        {
            UpdateCount++;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeLogs : ILogService
    {
        public List<(string action, string description, int? objectId)> Events { get; } = [];
        public Action? BeforeInfo { get; init; }
        public Task<OperationResult> LogInfoAsync(string objectType, string actionType, string description,
            int? objectId = null, CancellationToken cancellationToken = default)
        {
            BeforeInfo?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add((actionType, description, objectId));
            return Task.FromResult(OperationResult.Success());
        }
        public Task<OperationResult> LogErrorAsync(string objectType, string actionType, string description,
            int? objectId = null, CancellationToken cancellationToken = default)
        {
            Events.Add((actionType, description, objectId));
            return Task.FromResult(OperationResult.Success());
        }
        public Task<OperationResult<IReadOnlyList<LogEntry>>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<LogEntry>>.Success([]));
        public Task<OperationResult<IReadOnlyList<LogEntry>>> GetByDateRangeAsync(DateTime startDate, DateTime endDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<LogEntry>>.Success([]));
        public Task<OperationResult<IReadOnlyList<LogEntry>>> GetByActionTypeAsync(string actionType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<LogEntry>>.Success([]));
    }
}
