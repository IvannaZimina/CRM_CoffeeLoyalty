using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.interfaces.services;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Interfaces.Services;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.Services;
using Xunit;

namespace CoffeeLoyalty.Core.Tests;

// Unit tests covering the Drinks (Menu) section:
// role-based menu visibility (Cashier vs. Manager), drink creation, validation, activation status, and audit logging.
public class DrinkServiceTests
{
    // Tests that Cashiers querying available drinks receive only active menu items.
    [Fact]
    public async Task GetAvailableDrinksAsync_Cashier_ReturnsOnlyActiveDrinks()
    {
        // Arrange: seed repository with one active drink and one inactive seasonal drink.
        FakeDrinkRepository repo = new(
        [
            new Drink("Espresso", 2.50m, "Coffee", true, null) { Id = 1 },
            new Drink("Seasonal Latte", 4.50m, "Coffee", false, null) { Id = 2 }
        ]);

        DrinkService service = new(repo, FakeAuth(UserRole.Cashier), new FakeLogService());

        // Act: request available menu drinks as a Cashier.
        OperationResult<IReadOnlyList<Drink>> result = await service.GetAvailableDrinksAsync();

        // Assert: verify only the active drink ("Espresso") is returned.
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Single(result.Value!);
        Assert.Equal("Espresso", result.Value![0].DrinkName);
    }

    // Tests that Cashiers are forbidden from viewing inactive/archived drinks via GetAllDrinksAsync.
    [Fact]
    public async Task GetAllDrinksAsync_Cashier_DeniedAccess()
    {
        // Arrange: seed repository with active and inactive drinks under a Cashier session.
        FakeDrinkRepository repo = new(
        [
            new Drink("Espresso", 2.50m, "Coffee", true, null) { Id = 1 },
            new Drink("Archived Tea", 3.00m, "Tea", false, null) { Id = 2 }
        ]);

        FakeLogService logs = new();
        DrinkService service = new(repo, FakeAuth(UserRole.Cashier), logs);

        // Act: attempt to retrieve all drinks including inactive items as a Cashier.
        OperationResult<IReadOnlyList<Drink>> result = await service.GetAllDrinksAsync();

        // Assert: verify access is denied with FORBIDDEN error code.
        Assert.False(result.IsSuccess);
        Assert.Equal("FORBIDDEN", result.ErrorCode);
    }

    // Tests that Managers can retrieve all drinks, including inactive/archived items.
    [Fact]
    public async Task GetAllDrinksAsync_Manager_ReturnsAllDrinksIncludingInactive()
    {
        // Arrange: seed repository with active and inactive drinks under a Manager session.
        FakeDrinkRepository repo = new(
        [
            new Drink("Espresso", 2.50m, "Coffee", true, null) { Id = 1 },
            new Drink("Archived Tea", 3.00m, "Tea", false, null) { Id = 2 }
        ]);

        DrinkService service = new(repo, FakeAuth(UserRole.Manager), new FakeLogService());

        // Act: request the complete menu list as a Manager.
        OperationResult<IReadOnlyList<Drink>> result = await service.GetAllDrinksAsync();

        // Assert: verify both active and inactive drinks are returned.
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
    }

    // Tests that Cashiers cannot add new drinks to the menu and nothing is written to the repository.
    [Fact]
    public async Task AddDrinkAsync_Cashier_DeniedWithoutWritingToRepository()
    {
        // Arrange: initialize empty drink repository and Cashier session.
        FakeDrinkRepository repo = new();
        DrinkService service = new(repo, FakeAuth(UserRole.Cashier), new FakeLogService());

        // Act: attempt to add a new drink as a Cashier.
        OperationResult<Drink> result = await service.AddDrinkAsync(
            new Drink("Cappuccino", 3.80m, "Coffee", true, null));

        // Assert: verify request is rejected with FORBIDDEN and repository remains untouched.
        Assert.False(result.IsSuccess);
        Assert.Equal("FORBIDDEN", result.ErrorCode);
        Assert.Empty(repo.AddedDrinks);
    }

    // Tests that Managers can add a valid drink, persist it via repository, and emit an informational audit log.
    [Fact]
    public async Task AddDrinkAsync_ManagerWithValidDrink_AddsAndLogsInfo()
    {
        // Arrange: initialize empty repository, log spy, and Manager session.
        FakeDrinkRepository repo = new();
        FakeLogService logs = new();
        DrinkService service = new(repo, FakeAuth(UserRole.Manager), logs);

        // Act: add a valid Cappuccino menu item as a Manager.
        OperationResult<Drink> result = await service.AddDrinkAsync(
            new Drink("Cappuccino", 3.80m, "Coffee", true, null));

        // Assert: verify drink creation succeeded, was stored once, and recorded an audit log entry.
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Cappuccino", result.Value!.DrinkName);
        Assert.Single(repo.AddedDrinks);
        Assert.NotEmpty(logs.InfoEntries);
    }

    // Tests Drink domain model constructor guards against blank names/types and non-positive prices.
    [Theory]
    [InlineData("", 3.50, "Coffee")]
    [InlineData("Latte", 0, "Coffee")]
    [InlineData("Latte", -2.00, "Coffee")]
    [InlineData("Latte", 3.50, "")]
    public void Drink_WithInvalidFields_ThrowsValidationException(
        string name, decimal price, string type)
    {
        // Act & Assert: constructing a Drink with invalid fields throws ArgumentException / ArgumentOutOfRangeException.
        Assert.ThrowsAny<ArgumentException>(() =>
            new Drink(name, price, type, true, null));
    }

    // Tests that Managers can softly deactivate an existing drink and record an audit log entry.
    [Fact]
    public async Task SetActiveStatusAsync_Manager_UpdatesStatusAndLogsInfo()
    {
        // Arrange: seed an active drink with ID 5 under a Manager session.
        FakeDrinkRepository repo = new(
        [
            new Drink("Mocha", 4.20m, "Coffee", true, null) { Id = 5 }
        ]);
        FakeLogService logs = new();
        DrinkService service = new(repo, FakeAuth(UserRole.Manager), logs);

        // Act: deactivate drink ID 5.
        OperationResult result = await service.SetActiveStatusAsync(5, false);

        // Assert: verify IsActive flag was updated to false and an info audit entry was logged.
        Assert.True(result.IsSuccess);
        Assert.False(repo.GetItem(5)!.IsActive);
        Assert.NotEmpty(logs.InfoEntries);
    }

    // Helper creating a stub authentication service for the specified role (or logged out when null).
    private static IAuthService FakeAuth(UserRole? role) => new StubAuthService(role);

    // In-memory IAuthService test double simulating authenticated Cashier/Manager sessions.
    private sealed class StubAuthService(UserRole? role) : IAuthService
    {
        public bool IsAuthenticated => role.HasValue;
        public UserRole? CurrentRole => role;
        public Employee? CurrentEmployee => role.HasValue
            ? new Employee("TestUser", role.Value) { Id = role.Value == UserRole.Manager ? 2 : 3 }
            : null;
        public string? CurrentSessionId => role.HasValue ? "test-session" : null;

        public Task<OperationResult> LoginAsync(
            string employeeName, UserRole loginRole, string? password = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());

        public Task<OperationResult> LogoutAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());

        public bool HasPermission(UserRole requiredRole) =>
            role == UserRole.Manager || (role.HasValue && role.Value == requiredRole);
    }

    // In-memory ILogService spy capturing info and error audit entries for assertions.
    private sealed class FakeLogService : ILogService
    {
        public List<(string ObjectType, string ActionType, string Description, int? ObjectId)> InfoEntries { get; } = [];
        public List<(string ObjectType, string ActionType, string Description, int? ObjectId)> ErrorEntries { get; } = [];

        public Task<OperationResult> LogInfoAsync(
            string objectType, string actionType, string description,
            int? objectId = null, CancellationToken cancellationToken = default)
        {
            InfoEntries.Add((objectType, actionType, description, objectId));
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> LogErrorAsync(
            string objectType, string actionType, string description,
            int? objectId = null, CancellationToken cancellationToken = default)
        {
            ErrorEntries.Add((objectType, actionType, description, objectId));
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult<IReadOnlyList<LogEntry>>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<LogEntry>>.Success([]));

        public Task<OperationResult<IReadOnlyList<LogEntry>>> GetByDateRangeAsync(
            DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<LogEntry>>.Success([]));

        public Task<OperationResult<IReadOnlyList<LogEntry>>> GetByActionTypeAsync(
            string actionType, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<LogEntry>>.Success([]));
    }

    // In-memory IDrinkRepository test double simulating menu storage and active-status updates.
    private sealed class FakeDrinkRepository(IEnumerable<Drink>? seed = null) : IDrinkRepository
    {
        private readonly List<Drink> _items = seed?.ToList() ?? [];
        public List<Drink> AddedDrinks { get; } = [];

        public Drink? GetItem(int id) => _items.FirstOrDefault(d => d.Id == id);

        public Task<OperationResult<IReadOnlyList<Drink>>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<Drink>>.Success(_items.AsReadOnly()));

        public Task<OperationResult<IReadOnlyList<Drink>>> GetAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<Drink>>.Success(
                _items.Where(d => d.IsActive).ToList().AsReadOnly()));

        public Task<OperationResult<Drink>> GetByIdAsync(int drinkId, CancellationToken cancellationToken = default)
        {
            Drink? item = GetItem(drinkId);
            return Task.FromResult(item is not null
                ? OperationResult<Drink>.Success(item)
                : OperationResult<Drink>.Failure("Not found", "NOT_FOUND"));
        }

        public Task<OperationResult<Drink>> AddAsync(Drink drink, object? transaction,
            CancellationToken cancellationToken = default) => AddAsync(drink, cancellationToken);

        public Task<OperationResult<Drink>> AddAsync(Drink drink, CancellationToken cancellationToken = default)
        {
            if (drink.Id <= 0)
            {
                drink.Id = (_items.Count == 0 ? 0 : _items.Max(d => d.Id)) + 1;
            }

            _items.Add(drink);
            AddedDrinks.Add(drink);
            return Task.FromResult(OperationResult<Drink>.Success(drink));
        }

        public Task<OperationResult> UpdateAsync(Drink drink, CancellationToken cancellationToken = default)
        {
            int idx = _items.FindIndex(d => d.Id == drink.Id);
            if (idx < 0)
            {
                return Task.FromResult(OperationResult.Failure("Not found", "NOT_FOUND"));
            }

            _items[idx] = drink;
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> SetActiveStatusAsync(
            int drinkId, bool isActive, CancellationToken cancellationToken = default)
        {
            Drink? item = GetItem(drinkId);
            if (item is null)
            {
                return Task.FromResult(OperationResult.Failure("Not found", "NOT_FOUND"));
            }

            item.IsActive = isActive;
            return Task.FromResult(OperationResult.Success());
        }
    }
}