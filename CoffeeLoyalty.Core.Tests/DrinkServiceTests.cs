using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.interfaces.services;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Interfaces.Services;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace CoffeeLoyalty.Core.Tests;

public class DrinkServiceTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public async Task GetAvailableDrinksAsync_Cashier_ReturnsOnlyActiveDrinks()
    {
        FakeDrinkRepository repo = new(
        [
            new Drink("Espresso", 2.50m, "Coffee", true, null) { Id = 1 },
            new Drink("Seasonal Latte", 4.50m, "Coffee", false, null) { Id = 2 }
        ]);

        DrinkService service = new(repo, FakeAuth(UserRole.Cashier), new FakeLogService());

        OperationResult<IReadOnlyList<Drink>> result =
            await service.GetAvailableDrinksAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Single(result.Value!);
        Assert.Equal("Espresso", result.Value![0].DrinkName);
        Assert.All(result.Value!, drink => Assert.True(drink.IsActive));

        int activeInSource = 1;
        int inactiveInSource = 1;
        int returnedCount = result.Value!.Count;
        bool allReturnedDrinksActive = result.Value.All(drink => drink.IsActive);

        _output.WriteLine(TestReportTableBuilder.Build(
            "Available Menu Check (Cashier)",
            [
                ("User role", UserRole.Cashier.ToString(), "CASHIER"),
                ("Service result", result.IsSuccess.ToString(), "SUCCESS"),
                ("Active drinks in source", activeInSource.ToString(), "INPUT"),
                ("Inactive drinks in source", inactiveInSource.ToString(), "INPUT"),
                ("Returned drinks", returnedCount.ToString(), "EXPECTED: 1"),
                ("All returned drinks active", allReturnedDrinksActive.ToString(), "CHECKED"),
                ("Returned drink name", result.Value[0].DrinkName, "EXPECTED: Espresso")
            ]));
    }

    [Fact]
    public async Task GetAllDrinksAsync_Cashier_DeniedAccess()
    {
        FakeDrinkRepository repo = new(
        [
            new Drink("Espresso", 2.50m, "Coffee", true, null) { Id = 1 },
            new Drink("Archived Tea", 3.00m, "Tea", false, null) { Id = 2 }
        ]);

        FakeLogService logs = new();
        DrinkService service = new(repo, FakeAuth(UserRole.Cashier), logs);

        OperationResult<IReadOnlyList<Drink>> result =
            await service.GetAllDrinksAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("FORBIDDEN", result.ErrorCode);
        Assert.NotEmpty(logs.ErrorEntries);

        _output.WriteLine(TestReportTableBuilder.Build(
            "Full Menu Access Check (Cashier)",
            [
                ("User role", UserRole.Cashier.ToString(), "NOT MANAGER"),
                ("Service result", result.IsSuccess.ToString(), "DENIED"),
                ("Error code", result.ErrorCode ?? "none", "FORBIDDEN"),
                ("Repository drinks", repo.GetAllAsync().Result.Value!.Count.ToString(), "UNCHANGED"),
                ("Error log count", logs.ErrorEntries.Count.ToString(), "LOGGED"),
                ("Logged action", logs.ErrorEntries[0].ActionType, "RECORDED")
            ]));
    }

    [Fact]
    public async Task GetAllDrinksAsync_Manager_ReturnsAllDrinksIncludingInactive()
    {
        FakeDrinkRepository repo = new(
        [
            new Drink("Espresso", 2.50m, "Coffee", true, null) { Id = 1 },
            new Drink("Archived Tea", 3.00m, "Tea", false, null) { Id = 2 }
        ]);

        DrinkService service = new(repo, FakeAuth(UserRole.Manager), new FakeLogService());

        OperationResult<IReadOnlyList<Drink>> result =
            await service.GetAllDrinksAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value!.Count);

        int totalCount = result.Value.Count;
        int activeCount = result.Value.Count(drink => drink.IsActive);
        int inactiveCount = result.Value.Count(drink => !drink.IsActive);

        Assert.Equal(1, activeCount);
        Assert.Equal(1, inactiveCount);

        _output.WriteLine(TestReportTableBuilder.Build(
            "Full Menu Access Check (Manager)",
            [
                ("User role", UserRole.Manager.ToString(), "AUTHORIZED"),
                ("Service result", result.IsSuccess.ToString(), "SUCCESS"),
                ("Total drinks", totalCount.ToString(), "EXPECTED: 2"),
                ("Active drinks", activeCount.ToString(), "INCLUDED"),
                ("Inactive drinks", inactiveCount.ToString(), "INCLUDED"),
                ("All menu items returned", (totalCount == 2).ToString(), "CHECKED")
            ]));
    }

    [Fact]
    public async Task AddDrinkAsync_Cashier_DeniedWithoutWritingToRepository()
    {
        FakeDrinkRepository repo = new();
        FakeLogService logs = new();

        DrinkService service = new(repo, FakeAuth(UserRole.Cashier), logs);

        OperationResult<Drink> result = await service.AddDrinkAsync(
            new Drink("Cappuccino", 3.80m, "Coffee", true, null));

        Assert.False(result.IsSuccess);
        Assert.Equal("FORBIDDEN", result.ErrorCode);
        Assert.Empty(repo.AddedDrinks);
        Assert.NotEmpty(logs.ErrorEntries);

        _output.WriteLine(TestReportTableBuilder.Build(
            "Add Drink Access Check (Cashier)",
            [
                ("User role", UserRole.Cashier.ToString(), "NOT MANAGER"),
                ("Attempted drink", "Cappuccino", "INPUT"),
                ("Attempted price", "3.80", "INPUT"),
                ("Service result", result.IsSuccess.ToString(), "DENIED"),
                ("Error code", result.ErrorCode ?? "none", "FORBIDDEN"),
                ("Added drinks count", repo.AddedDrinks.Count.ToString(), "EXPECTED: 0"),
                ("Error log count", logs.ErrorEntries.Count.ToString(), "LOGGED"),
                ("Logged action", logs.ErrorEntries[0].ActionType, "RECORDED")
            ]));
    }

    [Fact]
    public async Task AddDrinkAsync_ManagerWithValidDrink_AddsAndLogsInfo()
    {
        FakeDrinkRepository repo = new();
        FakeLogService logs = new();

        DrinkService service = new(repo, FakeAuth(UserRole.Manager), logs);

        OperationResult<Drink> result = await service.AddDrinkAsync(
            new Drink("Cappuccino", 3.80m, "Coffee", true, null));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Cappuccino", result.Value!.DrinkName);
        Assert.Equal(3.80m, result.Value.DrinkPrice);
        Assert.Single(repo.AddedDrinks);
        Assert.NotEmpty(logs.InfoEntries);

        Drink addedDrink = result.Value;
        bool drinkHasGeneratedId = addedDrink.Id > 0;

        Assert.True(drinkHasGeneratedId);
        Assert.Equal(addedDrink.Id, repo.AddedDrinks[0].Id);

        _output.WriteLine(TestReportTableBuilder.Build(
            "Add Drink Check (Manager)",
            [
                ("User role", UserRole.Manager.ToString(), "AUTHORIZED"),
                ("Service result", result.IsSuccess.ToString(), "SUCCESS"),
                ("Drink name", addedDrink.DrinkName, "EXPECTED: Cappuccino"),
                ("Drink price", addedDrink.DrinkPrice.ToString("0.00"), "EXPECTED: 3.80"),
                ("Drink category", addedDrink.DrinkType, "EXPECTED: Coffee"),
                ("Generated ID", addedDrink.Id.ToString(), "CHECKED"),
                ("ID is valid", drinkHasGeneratedId.ToString(), "CHECKED"),
                ("Repository count", repo.AddedDrinks.Count.ToString(), "EXPECTED: 1"),
                ("Info log count", logs.InfoEntries.Count.ToString(), "LOGGED"),
                ("Logged action", logs.InfoEntries[0].ActionType, "RECORDED")
            ]));
    }

    [Theory]
    [InlineData("", 3.50, "Coffee")]
    [InlineData("Latte", 0, "Coffee")]
    [InlineData("Latte", -2.00, "Coffee")]
    [InlineData("Latte", 3.50, "")]
    public void Drink_WithInvalidFields_ThrowsValidationException(
        string name,
        decimal price,
        string type)
    {
        ArgumentException exception = Assert.ThrowsAny<ArgumentException>(() =>
            new Drink(name, price, type, true, null));

        _output.WriteLine(TestReportTableBuilder.Build(
            "Drink Model Validation Check",
            [
                ("Input name", string.IsNullOrEmpty(name) ? "<empty>" : name, "CHECKED"),
                ("Input price", price.ToString("0.00"), "CHECKED"),
                ("Input type", string.IsNullOrEmpty(type) ? "<empty>" : type, "CHECKED"),
                ("Exception type", exception.GetType().Name, "THROWN"),
                ("Validation result", "Invalid drink rejected", "EXPECTED")
            ]));
    }

    [Fact]
    public async Task SetActiveStatusAsync_Manager_UpdatesStatusAndLogsInfo()
    {
        FakeDrinkRepository repo = new(
        [
            new Drink("Mocha", 4.20m, "Coffee", true, null) { Id = 5 }
        ]);

        FakeLogService logs = new();
        DrinkService service = new(repo, FakeAuth(UserRole.Manager), logs);

        Drink? drinkBeforeUpdate = repo.GetItem(5);
        Assert.NotNull(drinkBeforeUpdate);

        bool previousStatus = drinkBeforeUpdate.IsActive;

        OperationResult result = await service.SetActiveStatusAsync(5, false);

        Drink? drinkAfterUpdate = repo.GetItem(5);

        Assert.True(result.IsSuccess);
        Assert.NotNull(drinkAfterUpdate);
        Assert.False(drinkAfterUpdate.IsActive);
        Assert.NotEmpty(logs.InfoEntries);

        _output.WriteLine(TestReportTableBuilder.Build(
            "Drink Activation Status Check",
            [
                ("User role", UserRole.Manager.ToString(), "AUTHORIZED"),
                ("Drink ID", "5", "TARGET"),
                ("Drink name", drinkAfterUpdate.DrinkName, "Mocha"),
                ("Previous status", previousStatus.ToString(), "ACTIVE"),
                ("Requested status", "False", "DEACTIVATE"),
                ("Service result", result.IsSuccess.ToString(), "SUCCESS"),
                ("Actual status", drinkAfterUpdate.IsActive.ToString(), "UPDATED"),
                ("Info log count", logs.InfoEntries.Count.ToString(), "LOGGED"),
                ("Logged action", logs.InfoEntries[0].ActionType, "RECORDED")
            ]));
    }

    private static IAuthService FakeAuth(UserRole? role) =>
        new StubAuthService(role);

    private sealed class StubAuthService(UserRole? role) : IAuthService
    {
        public bool IsAuthenticated => role.HasValue;

        public UserRole? CurrentRole => role;

        public Employee? CurrentEmployee => role.HasValue
            ? new Employee("TestUser", role.Value)
            {
                Id = role.Value == UserRole.Manager ? 2 : 3
            }
            : null;

        public string? CurrentSessionId => role.HasValue
            ? "test-session"
            : null;

        public Task<OperationResult> LoginAsync(
            string employeeName,
            UserRole loginRole,
            string? password = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());

        public Task<OperationResult> LogoutAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());

        public bool HasPermission(UserRole requiredRole) =>
            role == UserRole.Manager ||
            (role.HasValue && role.Value == requiredRole);
    }

    private sealed class FakeLogService : ILogService
    {
        public List<(string ObjectType, string ActionType, string Description, int? ObjectId)>
            InfoEntries
        { get; } = [];

        public List<(string ObjectType, string ActionType, string Description, int? ObjectId)>
            ErrorEntries
        { get; } = [];

        public Task<OperationResult> LogInfoAsync(
            string objectType,
            string actionType,
            string description,
            int? objectId = null,
            CancellationToken cancellationToken = default)
        {
            InfoEntries.Add((objectType, actionType, description, objectId));

            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> LogErrorAsync(
            string objectType,
            string actionType,
            string description,
            int? objectId = null,
            CancellationToken cancellationToken = default)
        {
            ErrorEntries.Add((objectType, actionType, description, objectId));

            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult<IReadOnlyList<LogEntry>>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                OperationResult<IReadOnlyList<LogEntry>>.Success([]));

        public Task<OperationResult<IReadOnlyList<LogEntry>>> GetByDateRangeAsync(
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                OperationResult<IReadOnlyList<LogEntry>>.Success([]));

        public Task<OperationResult<IReadOnlyList<LogEntry>>> GetByActionTypeAsync(
            string actionType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                OperationResult<IReadOnlyList<LogEntry>>.Success([]));
    }

    private sealed class FakeDrinkRepository(IEnumerable<Drink>? seed = null)
        : IDrinkRepository
    {
        private readonly List<Drink> _items = seed?.ToList() ?? [];

        public List<Drink> AddedDrinks { get; } = [];

        public Drink? GetItem(int id) =>
            _items.FirstOrDefault(drink => drink.Id == id);

        public Task<OperationResult<IReadOnlyList<Drink>>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                OperationResult<IReadOnlyList<Drink>>.Success(
                    _items.AsReadOnly()));

        public Task<OperationResult<IReadOnlyList<Drink>>> GetAvailableAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                OperationResult<IReadOnlyList<Drink>>.Success(
                    _items
                        .Where(drink => drink.IsActive)
                        .ToList()
                        .AsReadOnly()));

        public Task<OperationResult<Drink>> GetByIdAsync(
            int drinkId,
            CancellationToken cancellationToken = default)
        {
            Drink? item = GetItem(drinkId);

            return Task.FromResult(item is not null
                ? OperationResult<Drink>.Success(item)
                : OperationResult<Drink>.Failure("Not found", "NOT_FOUND"));
        }

        public Task<OperationResult<Drink>> AddAsync(
            Drink drink,
            CancellationToken cancellationToken = default) =>
            AddAsync(drink, null, cancellationToken);

        public Task<OperationResult<Drink>> AddAsync(
            Drink drink,
            object? transaction,
            CancellationToken cancellationToken = default)
        {
            if (drink.Id <= 0)
            {
                drink.Id = (_items.Count == 0 ? 0 : _items.Max(item => item.Id)) + 1;
            }

            _items.Add(drink);
            AddedDrinks.Add(drink);

            return Task.FromResult(OperationResult<Drink>.Success(drink));
        }

        public Task<OperationResult> UpdateAsync(
            Drink drink,
            CancellationToken cancellationToken = default)
        {
            int index = _items.FindIndex(item => item.Id == drink.Id);

            if (index < 0)
            {
                return Task.FromResult(
                    OperationResult.Failure("Not found", "NOT_FOUND"));
            }

            _items[index] = drink;

            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> SetActiveStatusAsync(
            int drinkId,
            bool isActive,
            CancellationToken cancellationToken = default)
        {
            Drink? item = GetItem(drinkId);

            if (item is null)
            {
                return Task.FromResult(
                    OperationResult.Failure("Not found", "NOT_FOUND"));
            }

            item.IsActive = isActive;

            return Task.FromResult(OperationResult.Success());
        }
    }
}

// Run this test file from the solution directory:
// dotnet test --filter "FullyQualifiedName~CoffeeLoyalty.Core.Tests.DrinkServiceTests"