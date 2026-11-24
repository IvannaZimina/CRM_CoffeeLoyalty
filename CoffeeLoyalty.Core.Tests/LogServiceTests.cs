using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Interfaces.Services;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.resources;
using CoffeeLoyalty.Core.Services;
using Xunit;

namespace CoffeeLoyalty.Core.Tests;

public class LogServiceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public async Task UndefinedAction_IsRejectedWithoutWriting(int value)
    {
        EmployeeSession session = new();
        FakeLogRepository repository = new();
        LogService service = new(repository, session);
        Assert.Equal("INVALID_LOG", (await service.LogInfoAsync("System", (LogActionType)value, "Invalid event.")).ErrorCode);
        Assert.Equal("INVALID_LOG", (await service.LogErrorAsync("System", (LogActionType)value, "Invalid event.")).ErrorCode);
        Assert.Empty(repository.Items);
    }

    [Fact]
    public void EmployeeSession_UsesOneEmployeeStateAndClearsPermissions()
    {
        EmployeeSession session = new();
        ICurrentEmployeeContext readOnly = session;

        Assert.False(readOnly.IsAuthenticated);
        Assert.Null(readOnly.EmployeeId);
        Assert.Null(readOnly.CurrentRole);

        session.SetCurrentEmployee(NewEmployee(2, UserRole.Manager));

        Assert.True(readOnly.IsAuthenticated);
        Assert.Equal(2, readOnly.EmployeeId);
        Assert.Equal(UserRole.Manager, readOnly.CurrentRole);
        Assert.NotNull(session.CurrentEmployee);

        session.Clear();

        Assert.False(readOnly.IsAuthenticated);
        Assert.Null(readOnly.EmployeeId);
        Assert.Null(readOnly.CurrentRole);
        Assert.Null(session.CurrentEmployee);
    }

    [Fact]
    public void EmployeeSession_RejectsInactiveOrUnsavedEmployees()
    {
        EmployeeSession session = new();
        Employee inactive = NewEmployee(2, UserRole.Manager);
        inactive.IsActive = false;

        Assert.Throws<ArgumentException>(() => session.SetCurrentEmployee(inactive));
        Assert.Throws<ArgumentException>(() => session.SetCurrentEmployee(
            new Employee("Unsaved", UserRole.Cashier)));
        Assert.False(session.IsAuthenticated);
    }

    [Fact]
    public async Task LogInfoAsync_UsesEmployeeIdAndAffectedObjectIdIndependently()
    {
        EmployeeSession session = SignedIn(2, UserRole.Cashier);
        FakeLogRepository repository = new();
        LogService service = new(repository, session);

        OperationResult result = await service.LogInfoAsync(
            "DRINKS", LogActionType.DrinkActivated, "Drink activated.", objectId: 15);

        Assert.True(result.IsSuccess);
        LogEntry saved = Assert.Single(repository.Items);
        Assert.Equal(2, saved.EmployeeId);
        Assert.Equal(15, saved.ObjectId);
        Assert.Equal("DrinkActivated", saved.ActionType);
        Assert.Equal<LogActionType?>(LogActionType.DrinkActivated, saved.KnownActionType);
        Assert.Equal("Drink activated.", saved.Description);
        Assert.Equal(DateTimeKind.Utc, saved.TimeStamp.Kind);
    }

    [Fact]
    public async Task LogErrorAsync_NoSession_StoresSystemEventWithoutActor()
    {
        EmployeeSession session = new();
        FakeLogRepository repository = new();
        LogService service = new(repository, session);

        OperationResult result = await service.LogErrorAsync(
            "System", LogActionType.ErrorStartup, "Startup failed.");

        Assert.True(result.IsSuccess);
        LogEntry saved = Assert.Single(repository.Items);
        Assert.Null(saved.EmployeeId);
        Assert.Null(saved.ObjectId);
    }

    [Fact]
    public async Task LogInfoAsync_AfterLogout_DoesNotReusePreviousManagerId()
    {
        EmployeeSession session = SignedIn(2, UserRole.Manager);
        FakeLogRepository repository = new();
        LogService service = new(repository, session);

        await service.LogInfoAsync("System", LogActionType.LogLogin, "Signed in.");
        session.Clear();
        await service.LogInfoAsync("System", LogActionType.LogStartup, "System event.");

        Assert.Equal(2, repository.Items.Count);
        Assert.Equal(2, repository.Items[0].EmployeeId);
        Assert.Null(repository.Items[1].EmployeeId);
    }

    [Fact]
    public async Task Manager_CanReadAllLogsAndUseBothFilters()
    {
        EmployeeSession session = SignedIn(2, UserRole.Manager);
        FakeLogRepository repository = new();
        LogService service = new(repository, session);

        DateTime time = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        repository.Items.Add(new LogEntry("DRINKS", "DrinkActivated", "Drink enabled",
            objectId: 15, timeStamp: time, employeeId: 2));
        repository.Items.Add(new LogEntry("System", "ErrorStartup", "System failed",
            timeStamp: time.AddDays(-2)));

        OperationResult<IReadOnlyList<LogEntry>> all = await service.GetAllAsync();
        OperationResult<IReadOnlyList<LogEntry>> byDate = await service.GetByDateRangeAsync(
            time.AddHours(-1), time.AddHours(1));
        OperationResult<IReadOnlyList<LogEntry>> byAction =
            await service.GetByActionTypeAsync(LogActionType.DrinkActivated);

        Assert.True(all.IsSuccess);
        Assert.Equal(2, all.Value!.Count);
        Assert.Single(byDate.Value!);
        Assert.Single(byAction.Value!);
        Assert.Equal(3, repository.ReadCount);
        Assert.Equal("DrinkActivated", repository.LastActionType);
    }

    [Theory]
    [InlineData(UserRole.Cashier)]
    [InlineData(null)]
    public async Task ManagerOnlyQueries_DenyOtherRolesOrNoSession(UserRole? role)
    {
        EmployeeSession session = role.HasValue
            ? SignedIn(3, role.Value)
            : new EmployeeSession();
        FakeLogRepository repository = new();
        LogService service = new(repository, session);

        OperationResult<IReadOnlyList<LogEntry>> all = await service.GetAllAsync();
        OperationResult<IReadOnlyList<LogEntry>> byDate = await service.GetByDateRangeAsync(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);
        OperationResult<IReadOnlyList<LogEntry>> byAction =
            await service.GetByActionTypeAsync(LogActionType.LogPurchase);

        Assert.Equal("ACCESS_DENIED", all.ErrorCode);
        Assert.Equal("ACCESS_DENIED", byDate.ErrorCode);
        Assert.Equal("ACCESS_DENIED", byAction.ErrorCode);
        Assert.Equal(0, repository.ReadCount);
    }

    [Fact]
    public async Task SwitchingFromManagerToCashier_RevokesLogReadPermission()
    {
        EmployeeSession session = SignedIn(2, UserRole.Manager);
        FakeLogRepository repository = new();
        LogService service = new(repository, session);

        Assert.True((await service.GetAllAsync()).IsSuccess);
        session.SetCurrentEmployee(NewEmployee(3, UserRole.Cashier));
        Assert.Equal("ACCESS_DENIED", (await service.GetAllAsync()).ErrorCode);
        Assert.Equal(1, repository.ReadCount);
    }

    [Fact]
    public async Task UnsafeDescriptions_AreReplacedWithSafeText()
    {
        FakeLogRepository repository = new();
        LogService service = new(repository, new EmployeeSession());

        Assert.True((await service.LogInfoAsync("System", LogActionType.LogStartup,
            "password=private-secret-value")).IsSuccess);
        Assert.True((await service.LogErrorAsync("System", LogActionType.ErrorStartup,
            "InvalidOperationException: internal exception\n   at C:\\private\\code.cs")).IsSuccess);

        Assert.Equal(2, repository.Items.Count);
        Assert.All(repository.Items, item =>
        {
            Assert.Equal(CoreResources.LogServiceDetailsRedacted, item.Description);
            Assert.DoesNotContain("private-secret-value", item.Description);
            Assert.DoesNotContain("code.cs", item.Description);
        });
    }

    [Fact]
    public async Task InvalidLogInput_IsRejectedBeforeStorage()
    {
        FakeLogRepository repository = new();
        LogService service = new(repository, new EmployeeSession());

        OperationResult blank = await service.LogInfoAsync(" ", LogActionType.LogStartup, "Description");
        OperationResult badId = await service.LogErrorAsync("System", LogActionType.ErrorStartup, "Description", 0);

        Assert.Equal("INVALID_LOG", blank.ErrorCode);
        Assert.Equal("INVALID_LOG", badId.ErrorCode);
        Assert.Empty(repository.Items);
    }

    [Fact]
    public async Task RepositoryFailures_DoNotExposeExceptionDetails()
    {
        EmployeeSession session = SignedIn(2, UserRole.Manager);
        FakeLogRepository repository = new() { ThrowOnWrite = true, ThrowOnRead = true };
        LogService service = new(repository, session);

        OperationResult write = await service.LogErrorAsync(
            "System", LogActionType.ErrorStartup, "Safe event description.");
        OperationResult<IReadOnlyList<LogEntry>> read = await service.GetAllAsync();

        Assert.Equal("LOG_WRITE_FAILED", write.ErrorCode);
        Assert.Equal("LOG_READ_FAILED", read.ErrorCode);
        Assert.DoesNotContain("private-password-value", write.ErrorMessage!);
        Assert.DoesNotContain("private-password-value", read.ErrorMessage!);
    }

    [Fact]
    public async Task InvalidFilters_AreReturnedAsStructuredFailures()
    {
        LogService service = new(new FakeLogRepository(), SignedIn(2, UserRole.Manager));
        DateTime time = DateTime.UtcNow;

        OperationResult<IReadOnlyList<LogEntry>> range =
            await service.GetByDateRangeAsync(time.AddDays(1), time);
        OperationResult<IReadOnlyList<LogEntry>> action =
            await service.GetByActionTypeAsync((LogActionType)0);

        Assert.Equal("INVALID_FILTER", range.ErrorCode);
        Assert.Equal("INVALID_FILTER", action.ErrorCode);
    }

    [Fact]
    public async Task CancelledRequests_PropagateCancellation()
    {
        using CancellationTokenSource source = new();
        source.Cancel();
        LogService service = new(new FakeLogRepository(), SignedIn(2, UserRole.Manager));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetAllAsync(source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.LogInfoAsync("System", LogActionType.LogStartup, "Test", cancellationToken: source.Token));
    }

    private static Employee NewEmployee(int id, UserRole role) =>
        new($"Employee {id}", role) { Id = id };

    private static EmployeeSession SignedIn(int id, UserRole role)
    {
        EmployeeSession session = new();
        session.SetCurrentEmployee(NewEmployee(id, role));
        return session;
    }

    private sealed class FakeLogRepository : ILogRepository
    {
        public List<LogEntry> Items { get; } = [];
        public int ReadCount { get; private set; }
        public bool ThrowOnWrite { get; set; }
        public bool ThrowOnRead { get; set; }
        public string? LastActionType { get; private set; }

        public Task<int> AddAsync(LogEntry logEntry, CancellationToken cancellationToken = default)
        {
            if (ThrowOnWrite)
            {
                throw new InvalidOperationException("private-password-value");
            }

            logEntry.Id = Items.Count + 1;
            Items.Add(logEntry);
            return Task.FromResult(logEntry.Id);
        }

        public Task<IReadOnlyList<LogEntry>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            BeforeRead();
            return Task.FromResult<IReadOnlyList<LogEntry>>(Items.AsReadOnly());
        }

        public Task<IReadOnlyList<LogEntry>> GetByDateRangeAsync(
            DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
        {
            BeforeRead();
            return Task.FromResult<IReadOnlyList<LogEntry>>(
                Items.Where(item => item.TimeStamp >= startDate && item.TimeStamp <= endDate).ToList());
        }

        public Task<IReadOnlyList<LogEntry>> GetByActionTypeAsync(
            string actionType, CancellationToken cancellationToken = default)
        {
            BeforeRead();
            LastActionType = actionType;
            return Task.FromResult<IReadOnlyList<LogEntry>>(
                Items.Where(item => item.ActionType == actionType).ToList());
        }

        private void BeforeRead()
        {
            ReadCount++;
            if (ThrowOnRead)
            {
                throw new InvalidOperationException("private-password-value");
            }
        }
    }
}
