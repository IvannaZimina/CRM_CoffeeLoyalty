using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.interfaces.services;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Interfaces.Services;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.Services;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace CoffeeLoyalty.Core.Tests;

// Verifies Manager role enforcement, JSON export, import validation, and import persistence in DataTransferService.
public class DataTransferServiceAccessTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    // Formats a clean ASCII table for test console output so checked items are easy to read.
    private static string BuildReportTable(string title, IEnumerable<(string Check, string Value, string Status)> rows)
    {
        List<(string Check, string Value, string Status)> list = rows.ToList();
        int col1 = Math.Max("Check".Length, list.Count == 0 ? 0 : list.Max(r => r.Check.Length));
        int col2 = Math.Max("Value".Length, list.Count == 0 ? 0 : list.Max(r => r.Value.Length));
        int col3 = Math.Max("Status".Length, list.Count == 0 ? 0 : list.Max(r => r.Status.Length));

        string border = $"+-{new string('-', col1)}-+-{new string('-', col2)}-+-{new string('-', col3)}-+";
        StringBuilder sb = new();
        sb.AppendLine();
        sb.AppendLine($"=== {title} ===");
        sb.AppendLine(border);
        sb.AppendLine($"| {"Check".PadRight(col1)} | {"Value".PadRight(col2)} | {"Status".PadRight(col3)} |");
        sb.AppendLine(border);

        foreach ((string check, string value, string status) in list)
        {
            sb.AppendLine($"| {check.PadRight(col1)} | {value.PadRight(col2)} | {status.PadRight(col3)} |");
        }

        sb.AppendLine(border);
        return sb.ToString();
    }

    // Step 3a: checks that unauthenticated calls are denied, logged, and do not touch repositories or files.
    [Fact]
    public async Task Methods_WhenUnauthenticated_DenyAccessAndLogError()
    {
        // Arrange: create a fake auth service with no active session.
        FakeAuthService authService = new()
        {
            IsAuthenticated = false,
            CurrentRole = null,
            CurrentEmployee = null
        };

        FakeLogService logService = new();
        FakeDrinkRepository drinkRepo = new();
        FakeCustomerRepository customerRepo = new();
        FakeCardRepository cardRepo = new();
        FakePurchaseRepository purchaseRepo = new();
        FakeLogRepository logRepo = new();

        DataTransferService service = new(
            drinkRepo,
            customerRepo,
            cardRepo,
            purchaseRepo,
            logRepo,
            authService,
            logService,
            new SqliteConnectionFactory(":memory:"));

        string tempFile = Path.Combine(Path.GetTempPath(), $"denied_{Guid.NewGuid():N}.json");

        try
        {
            // Act: invoke all three operations without authentication.
            OperationResult<int> exportResult = await service.ExportToJsonAsync(tempFile);
            OperationResult<IReadOnlyList<string>> validateResult = await service.ValidateImportAsync(tempFile);
            OperationResult<int> importResult = await service.ImportFromJsonAsync(tempFile);

            // Assert: all operations fail with ACCESS_DENIED.
            Assert.False(exportResult.IsSuccess);
            Assert.Equal("ACCESS_DENIED", exportResult.ErrorCode);

            Assert.False(validateResult.IsSuccess);
            Assert.Equal("ACCESS_DENIED", validateResult.ErrorCode);

            Assert.False(importResult.IsSuccess);
            Assert.Equal("ACCESS_DENIED", importResult.ErrorCode);

            // Assert: error was logged via ILogService and no files or repositories were touched.
            Assert.Equal(3, logService.ErrorEntries.Count);
            Assert.All(logService.ErrorEntries, entry => Assert.Equal("ErrorAccessDenied", entry.ActionType));
            Assert.False(File.Exists(tempFile));
            Assert.False(drinkRepo.GetAllCalled);
            Assert.False(customerRepo.GetAllCalled);
            Assert.False(cardRepo.GetAllCalled);
            Assert.False(purchaseRepo.GetAllCalled);
            Assert.False(logRepo.GetAllCalled);

            // Print readable summary table to test output.
            _output.WriteLine(BuildReportTable("Unauthenticated Access Check",
            [
                ("Auth state", "IsAuthenticated = false", "OK"),
                ("ExportToJsonAsync", exportResult.ErrorCode ?? "none", "DENIED AS EXPECTED"),
                ("ValidateImportAsync", validateResult.ErrorCode ?? "none", "DENIED AS EXPECTED"),
                ("ImportFromJsonAsync", importResult.ErrorCode ?? "none", "DENIED AS EXPECTED"),
                ("ErrorAccessDenied logs", logService.ErrorEntries.Count.ToString(), "LOGGED OK"),
                ("Repositories touched", "none", "SKIPPED OK")
            ]));
        }
        finally
        {
            // Clean up temporary file if it was somehow created.
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    // Step 3b: checks that a non-Manager role (for example Cashier) is denied access.
    [Fact]
    public async Task Methods_WhenRoleIsNotManager_DenyAccessAndLogError()
    {
        // Arrange: simulate an authenticated Cashier session.
        FakeAuthService authService = new()
        {
            IsAuthenticated = true,
            CurrentRole = UserRole.Cashier,
            CurrentEmployee = null
        };

        FakeLogService logService = new();
        FakeDrinkRepository drinkRepo = new();
        FakeCustomerRepository customerRepo = new();
        FakeCardRepository cardRepo = new();
        FakePurchaseRepository purchaseRepo = new();
        FakeLogRepository logRepo = new();

        DataTransferService service = new(
            drinkRepo,
            customerRepo,
            cardRepo,
            purchaseRepo,
            logRepo,
            authService,
            logService,
            new SqliteConnectionFactory(":memory:"));

        string dummyPath = Path.Combine(Path.GetTempPath(), $"cashier_{Guid.NewGuid():N}.json");

        // Act: call ExportToJsonAsync with Cashier role.
        OperationResult<int> exportResult = await service.ExportToJsonAsync(dummyPath);

        // Assert: access is denied and logged.
        Assert.False(exportResult.IsSuccess);
        Assert.Equal("ACCESS_DENIED", exportResult.ErrorCode);
        Assert.Single(logService.ErrorEntries);
        Assert.Equal("ErrorAccessDenied", logService.ErrorEntries[0].ActionType);
        Assert.False(drinkRepo.GetAllCalled);

        // Print readable summary table to test output.
        _output.WriteLine(BuildReportTable("Non-Manager Role Check",
        [
            ("Role", UserRole.Cashier.ToString(), "NOT MANAGER"),
            ("ExportToJsonAsync", exportResult.ErrorCode ?? "none", "DENIED AS EXPECTED"),
            ("ErrorAccessDenied log", logService.ErrorEntries[0].ActionType, "LOGGED OK"),
            ("DrinkRepository.GetAll", drinkRepo.GetAllCalled.ToString(), "NOT CALLED OK")
        ]));
    }

    // Step 4: verifies that an authenticated Manager exports all datasets to JSON and logs LogExportJson.
    [Fact]
    public async Task ExportToJsonAsync_WhenManagerAuthenticated_ExportsAllSectionsAndLogsInfo()
    {
        // Arrange: authenticate as Manager and seed sample domain records in repositories.
        FakeAuthService authService = new()
        {
            IsAuthenticated = true,
            CurrentRole = UserRole.Manager,
            CurrentEmployee = null
        };

        FakeLogService logService = new();

        Drink drink1 = new(
            drinkName: "Latte",
            drinkPrice: 3.50m,
            drinkType: "Coffee",
            isActive: true,
            drinkImageLink: null)
        {
            Id = 1
        };

        Customer customer1 = new(
            categoryId: 1,
            customerName: "Anna",
            bonusBalance: 25,
            birthDate: null,
            isActive: true,
            timeStamp: new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc))
        {
            Id = 10
        };

        LoyaltyCard card1 = new(
            customerId: 10,
            isActive: true)
        {
            Id = 100
        };

        Purchase purchase1 = new(
            cardId: 100,
            employeeId: 1,
            totalAmount: 3.50m,
            bonusesSpent: 0,
            bonusesEarned: 1,
            timeStamp: new DateTime(2026, 1, 10, 10, 5, 0, DateTimeKind.Utc))
        {
            Id = 200
        };

        LogEntry logEntry1 = new(
            objectId: 15,
            objectType: "Auth",
            actionType: "LogLogin",
            description: "Manager signed in.",
            timeStamp: new DateTime(2026, 1, 10, 9, 55, 0, DateTimeKind.Utc),
            employeeId: 2)
        {
            Id = 300
        };

        FakeDrinkRepository drinkRepo = new([drink1]);
        FakeCustomerRepository customerRepo = new([customer1]);
        FakeCardRepository cardRepo = new([card1]);
        FakePurchaseRepository purchaseRepo = new([purchase1]);
        FakeLogRepository logRepo = new([logEntry1]);

        DataTransferService service = new(
            drinkRepo,
            customerRepo,
            cardRepo,
            purchaseRepo,
            logRepo,
            authService,
            logService,
            new SqliteConnectionFactory(":memory:"));

        string tempFile = Path.Combine(Path.GetTempPath(), $"export_step4_{Guid.NewGuid():N}.json");

        try
        {
            // Act: export all datasets to the temporary JSON file.
            OperationResult<int> result = await service.ExportToJsonAsync(tempFile);

            // Assert: result succeeds and equals total exported record count across all 5 sections.
            Assert.True(result.IsSuccess);
            Assert.Equal(5, result.Value);
            Assert.True(File.Exists(tempFile));

            // Assert: JSON file deserializes into DataTransferPayloadDto with expected items.
            string json = await File.ReadAllTextAsync(tempFile);
            DataTransferPayloadDto? payload = JsonSerializer.Deserialize<DataTransferPayloadDto>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(payload);
            Assert.Single(payload!.Drinks);
            Assert.Equal("Latte", payload.Drinks[0].DrinkName);
            Assert.Single(payload.Customers);
            Assert.Equal("Anna", payload.Customers[0].CustomerName);
            Assert.Single(payload.Cards);
            Assert.Equal(10, payload.Cards[0].CustomerId);
            Assert.Single(payload.Purchases);
            Assert.Equal(3.50m, payload.Purchases[0].TotalAmount);
            Assert.Single(payload.Logs);
            Assert.Equal("LogLogin", payload.Logs[0].ActionType);
            Assert.Equal(2, payload.Logs[0].EmployeeId);
            Assert.Equal(15, payload.Logs[0].ObjectId);
            Assert.Contains("\"EmployeeId\": 2", json);

            // Assert: info log entry with actionType LogExportJson was recorded.
            Assert.Single(logService.InfoEntries);
            Assert.Equal("LogExportJson", logService.InfoEntries[0].ActionType);

            // Print exported dataset summary table.
            _output.WriteLine(BuildReportTable("JSON Export Check (Manager)",
            [
                ("Drinks", $"{payload.Drinks.Count} (Latte, 3.50)", "EXPORTED OK"),
                ("Customers", $"{payload.Customers.Count} (Anna, bonus 25)", "EXPORTED OK"),
                ("Cards", $"{payload.Cards.Count} (customerId 10)", "EXPORTED OK"),
                ("Purchases", $"{payload.Purchases.Count} (total 3.50)", "EXPORTED OK"),
                ("Logs", $"{payload.Logs.Count} (LogLogin)", "EXPORTED OK"),
                ("Total records", result.Value.ToString(), "MATCHED OK"),
                ("Info log", logService.InfoEntries[0].ActionType, "LOGGED OK")
            ]));
        }
        finally
        {
            // Clean up temporary export file after test completes.
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    // Step 5a: verifies that ValidateImportAsync succeeds with a warning when Purchases or Logs are present.
    [Fact]
    public async Task ValidateImportAsync_WhenValidPayloadWithIgnoredHistory_ReturnsSuccessWithWarning()
    {
        // Arrange: authenticate as Manager and prepare a valid import payload containing Purchases and Logs.
        FakeAuthService authService = new()
        {
            IsAuthenticated = true,
            CurrentRole = UserRole.Manager,
            CurrentEmployee = null
        };

        FakeLogService logService = new();
        FakeDrinkRepository drinkRepo = new();
        FakeCustomerRepository customerRepo = new();
        FakeCardRepository cardRepo = new();
        FakePurchaseRepository purchaseRepo = new();
        FakeLogRepository logRepo = new();

        DataTransferService service = new(
            drinkRepo,
            customerRepo,
            cardRepo,
            purchaseRepo,
            logRepo,
            authService,
            logService,
            new SqliteConnectionFactory(":memory:"));

        DataTransferPayloadDto payload = new()
        {
            Version = "1.0",
            Drinks =
            [
                new DrinkTransferDto
                {
                    DrinkName = "Cappuccino",
                    DrinkPrice = 4.00m,
                    DrinkType = "Coffee",
                    IsActive = true
                }
            ],
            Customers =
            [
                new CustomerTransferDto
                {
                    Id = 50,
                    CategoryId = 1,
                    CustomerName = "Mark",
                    BonusBalance = 10,
                    IsActive = true
                }
            ],
            Cards =
            [
                new CardTransferDto
                {
                    CustomerId = 50,
                    IsActive = true
                }
            ],
            Purchases =
            [
                new PurchaseTransferDto { Id = 1, TotalAmount = 4.00m }
            ],
            Logs =
            [
                new LogTransferDto { Id = 1, ActionType = "LogPurchase" }
            ]
        };

        string tempFile = Path.Combine(Path.GetTempPath(), $"validate_ok_{Guid.NewGuid():N}.json");
        JsonNode legacyPayload = JsonSerializer.SerializeToNode(payload)!;
        legacyPayload["Logs"]![0]!.AsObject().Remove("EmployeeId");
        await File.WriteAllTextAsync(tempFile, legacyPayload.ToJsonString());

        try
        {
            // Act: validate the import file.
            OperationResult<IReadOnlyList<string>> result = await service.ValidateImportAsync(tempFile);

            // Assert: validation succeeds and returns a warning about ignored Purchases/Logs.
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.NotEmpty(result.Value!);
            Assert.Empty(logService.ErrorEntries);

            // Print validation result table.
            _output.WriteLine(BuildReportTable("Import Validation (Valid + History Warning)",
            [
                ("Drink item", "Cappuccino (4.00)", "VALID OK"),
                ("Customer item", "Mark (id=50)", "VALID OK"),
                ("Card item", "customerId=50", "VALID OK"),
                ("Warnings count", result.Value!.Count.ToString(), "WARNING RETURNED"),
                ("Validation errors", logService.ErrorEntries.Count.ToString(), "NONE OK")
            ]));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    // Step 5b: verifies that ValidateImportAsync rejects invalid data (duplicate drink name and missing customer reference).
    [Fact]
    public async Task ValidateImportAsync_WhenInvalidData_ReturnsFailureAndLogsError()
    {
        // Arrange: seed an existing drink "Espresso" and prepare invalid import items.
        FakeAuthService authService = new()
        {
            IsAuthenticated = true,
            CurrentRole = UserRole.Manager,
            CurrentEmployee = null
        };

        FakeLogService logService = new();
        Drink existingDrink = new("Espresso", 2.50m, "Coffee", true, null) { Id = 1 };
        FakeDrinkRepository drinkRepo = new([existingDrink]);
        FakeCustomerRepository customerRepo = new();
        FakeCardRepository cardRepo = new();
        FakePurchaseRepository purchaseRepo = new();
        FakeLogRepository logRepo = new();

        DataTransferService service = new(
            drinkRepo,
            customerRepo,
            cardRepo,
            purchaseRepo,
            logRepo,
            authService,
            logService,
            new SqliteConnectionFactory(":memory:"));

        DataTransferPayloadDto invalidPayload = new()
        {
            Version = "1.0",
            Drinks =
            [
                new DrinkTransferDto
                {
                    DrinkName = "Espresso",
                    DrinkPrice = -1m,
                    DrinkType = "Coffee"
                }
            ],
            Customers = [],
            Cards =
            [
                new CardTransferDto
                {
                    CustomerId = 999,
                    IsActive = true
                }
            ]
        };

        string tempFile = Path.Combine(Path.GetTempPath(), $"validate_bad_{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(tempFile, JsonSerializer.Serialize(invalidPayload));

        try
        {
            // Act: validate invalid payload.
            OperationResult<IReadOnlyList<string>> result = await service.ValidateImportAsync(tempFile);

            // Assert: validation fails with VALIDATION_FAILED and logs ErrorImportValidation.
            Assert.False(result.IsSuccess);
            Assert.Equal("VALIDATION_FAILED", result.ErrorCode);
            Assert.Single(logService.ErrorEntries);
            Assert.Equal("ErrorImportValidation", logService.ErrorEntries[0].ActionType);

            // Print rejected items table.
            _output.WriteLine(BuildReportTable("Import Validation (Invalid Data)",
            [
                ("Duplicate drink", "Espresso", "REJECTED OK"),
                ("Negative price", "-1", "REJECTED OK"),
                ("Missing customer", "customerId=999", "REJECTED OK"),
                ("Result code", result.ErrorCode ?? "none", "FAILED AS EXPECTED"),
                ("Error log", logService.ErrorEntries[0].ActionType, "LOGGED OK")
            ]));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    // Step 6: verifies that ImportFromJsonAsync imports drinks, customers, and remapped cards, then logs LogImportJson.
    [Fact]
    public async Task ImportFromJsonAsync_WhenValidPayload_ImportsEntitiesAndMapsCustomerIds()
    {
        // Arrange: authenticate as Manager and prepare a valid import payload with source Customer Id = 77.
        FakeAuthService authService = new()
        {
            IsAuthenticated = true,
            CurrentRole = UserRole.Manager,
            CurrentEmployee = null
        };

        FakeLogService logService = new();
        FakeDrinkRepository drinkRepo = new();
        FakeCustomerRepository customerRepo = new([], nextId: 500);
        FakeCardRepository cardRepo = new();
        FakePurchaseRepository purchaseRepo = new();
        FakeLogRepository logRepo = new();

        DataTransferService service = new(
            drinkRepo,
            customerRepo,
            cardRepo,
            purchaseRepo,
            logRepo,
            authService,
            logService,
            new SqliteConnectionFactory(":memory:"));

        DataTransferPayloadDto payload = new()
        {
            Version = "1.0",
            Drinks =
            [
                new DrinkTransferDto
                {
                    DrinkName = "Americano",
                    DrinkPrice = 3.00m,
                    DrinkType = "Coffee",
                    IsActive = true
                }
            ],
            Customers =
            [
                new CustomerTransferDto
                {
                    Id = 77,
                    CategoryId = 1,
                    CustomerName = "Elena",
                    BonusBalance = 15,
                    IsActive = true
                }
            ],
            Cards =
            [
                new CardTransferDto
                {
                    CustomerId = 77,
                    IsActive = true
                }
            ]
        };

        string tempFile = Path.Combine(Path.GetTempPath(), $"import_step6_{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(tempFile, JsonSerializer.Serialize(payload));

        try
        {
            // Act: execute import into repositories.
            OperationResult<int> result = await service.ImportFromJsonAsync(tempFile);

            // Assert: 3 records imported (1 drink, 1 customer, 1 card) and mapped to new customer ID 500.
            Assert.True(result.IsSuccess);
            Assert.Equal(3, result.Value);
            Assert.Single(drinkRepo.AddedDrinks);
            Assert.Single(customerRepo.AddedCustomers);
            Assert.Single(cardRepo.AddedCards);
            Assert.Equal(500, cardRepo.AddedCards[0].CustomerId);
            Assert.Single(logService.InfoEntries);
            Assert.Equal("LogImportJson", logService.InfoEntries[0].ActionType);

            // Print imported items and ID mapping table.
            _output.WriteLine(BuildReportTable("JSON Import & ID Mapping Check",
            [
                ("Imported drink", "Americano (3.00)", "IMPORTED OK"),
                ("Imported customer", "Elena (src Id 77 -> db Id 500)", "IMPORTED OK"),
                ("Imported card", "linked to customerId = 500", "REMAPPED OK"),
                ("Total imported", result.Value.ToString(), "MATCHED OK"),
                ("Info log", logService.InfoEntries[0].ActionType, "LOGGED OK")
            ]));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    // Lightweight fake IAuthService matching the exact IAuthService contract.
    private sealed class FakeAuthService : IAuthService
    {
        public bool IsAuthenticated { get; set; }
        public UserRole? CurrentRole { get; set; }
        public Employee? CurrentEmployee { get; set; }
        public string? CurrentSessionId => IsAuthenticated ? "session-1" : null;

        public Task<OperationResult> LoginAsync(
            string employeeName,
            UserRole role,
            string? password = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Failure("Not used in test."));

        public Task<OperationResult> LogoutAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());

        public bool HasPermission(UserRole requiredRole) =>
            IsAuthenticated && CurrentRole == requiredRole;
    }

    // Lightweight fake ILogService matching the exact ILogService contract.
    private sealed class FakeLogService : ILogService
    {
        public List<(string ObjectType, string ActionType, string Description, int? ObjectId)> InfoEntries { get; } = [];
        public List<(string ObjectType, string ActionType, string Description, int? ObjectId)> ErrorEntries { get; } = [];

        public Task<OperationResult> LogInfoAsync(
            string objectType,
            LogActionType actionType,
            string description,
            int? objectId = null,
            CancellationToken cancellationToken = default)
        {
            InfoEntries.Add((objectType, actionType.ToString(), description, objectId));
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> LogErrorAsync(
            string objectType,
            LogActionType actionType,
            string description,
            int? objectId = null,
            CancellationToken cancellationToken = default)
        {
            ErrorEntries.Add((objectType, actionType.ToString(), description, objectId));
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult<IReadOnlyList<LogEntry>>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<LogEntry>>.Success([]));

        public Task<OperationResult<IReadOnlyList<LogEntry>>> GetByDateRangeAsync(
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<LogEntry>>.Success([]));

        public Task<OperationResult<IReadOnlyList<LogEntry>>> GetByActionTypeAsync(
            LogActionType actionType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<LogEntry>>.Success([]));
    }

    // Fake IDrinkRepository supporting both export reads and import writes.
    private sealed class FakeDrinkRepository(IReadOnlyList<Drink>? items = null) : IDrinkRepository
    {
        private readonly List<Drink> _items = items?.ToList() ?? [];
        public bool GetAllCalled { get; private set; }
        public List<Drink> AddedDrinks { get; } = [];

        public Task<OperationResult<IReadOnlyList<Drink>>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            GetAllCalled = true;
            return Task.FromResult(OperationResult<IReadOnlyList<Drink>>.Success(_items));
        }

        public Task<OperationResult<IReadOnlyList<Drink>>> GetAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<Drink>>.Success(_items));

        public Task<OperationResult<Drink>> GetByIdAsync(int drinkId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<Drink>.Failure("Not found."));

        public Task<OperationResult<Drink>> AddAsync(Drink drink, object? transaction,
            CancellationToken cancellationToken = default) => AddAsync(drink, cancellationToken);

        public Task<OperationResult<Drink>> AddAsync(Drink drink, CancellationToken cancellationToken = default)
        {
            // Assign a synthetic ID if none was set so callers see a persisted drink.
            if (drink.Id <= 0)
            {
                drink.Id = _items.Count + 1;
            }

            _items.Add(drink);
            AddedDrinks.Add(drink);
            return Task.FromResult(OperationResult<Drink>.Success(drink));
        }

        public Task<OperationResult> UpdateAsync(Drink drink, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());

        public Task<OperationResult> SetActiveStatusAsync(int drinkId, bool isActive, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());
    }

    // Fake ICustomerRepository supporting export reads, import writes, and ID generation.
    private sealed class FakeCustomerRepository(IReadOnlyList<Customer>? items = null, int nextId = 1) : ICustomerRepository
    {
        private readonly List<Customer> _items = items?.ToList() ?? [];
        private int _nextId = nextId;
        public bool GetAllCalled { get; private set; }
        public List<Customer> AddedCustomers { get; } = [];

        public Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            GetAllCalled = true;
            return Task.FromResult<IReadOnlyList<Customer>>(_items);
        }

        public Task<Customer?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.FirstOrDefault(c => c.Id == id));

        public Task<Customer?> GetByCardAsync(int cardId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Customer?>(null);

        public Task<int> AddAsync(Customer customer, CancellationToken cancellationToken = default)
        {
            // Assign and return a new database ID when customer.Id is not yet set.
            int assignedId = customer.Id > 0 ? customer.Id : _nextId++;
            customer.Id = assignedId;
            _items.Add(customer);
            AddedCustomers.Add(customer);
            return Task.FromResult(assignedId);
        }

        // Supports the new repository overload in existing test doubles.
        public Task<int> AddAsync(
            Customer customer,
            object? transaction,
            CancellationToken cancellationToken = default) => AddAsync(customer, cancellationToken);

        public Task<bool> UpdateAsync(Customer customer, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        // Preserves existing fake behavior without simulating SQLite transactions.
        public Task<bool> UpdateAsync(
            Customer customer,
            object? transaction,
            CancellationToken cancellationToken = default)
        {
            if (transaction is not null)
            {
                throw new NotSupportedException();
            }

            return UpdateAsync(customer, cancellationToken);
        }

        public Task<bool> SetActiveStatusAsync(int customerId, bool isActive, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    // Fake ICardRepository supporting export reads and import writes.
    private sealed class FakeCardRepository(IReadOnlyList<LoyaltyCard>? items = null) : ICardRepository
    {
        private readonly List<LoyaltyCard> _items = items?.ToList() ?? [];
        public bool GetAllCalled { get; private set; }
        public List<LoyaltyCard> AddedCards { get; } = [];

        public Task<IReadOnlyList<LoyaltyCard>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            GetAllCalled = true;
            return Task.FromResult<IReadOnlyList<LoyaltyCard>>(_items);
        }

        public Task<LoyaltyCard?> GetByIdAsync(int cardId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.FirstOrDefault(c => c.Id == cardId));

        public Task<IReadOnlyList<LoyaltyCard>> GetByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LoyaltyCard>>(_items.Where(c => c.CustomerId == customerId).ToList());

        public Task<int> AddAsync(LoyaltyCard card, CancellationToken cancellationToken = default)
        {
            int assignedId = card.Id > 0 ? card.Id : _items.Count + 1;
            card.Id = assignedId;
            _items.Add(card);
            AddedCards.Add(card);
            return Task.FromResult(assignedId);
        }

        // Supports the transaction-aware interface overload.
        public Task<int> AddAsync(
            LoyaltyCard card,
            object? transaction,
            CancellationToken cancellationToken = default) => AddAsync(card, cancellationToken);

        public Task<bool> SetActiveStatusAsync(int cardId, bool isActive, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    // Fake IPurchaseRepository returning configured items for export tests.
    private sealed class FakePurchaseRepository(IReadOnlyList<Purchase>? items = null) : IPurchaseRepository
    {
        private readonly IReadOnlyList<Purchase> _items = items ?? [];
        public bool GetAllCalled { get; private set; }

        public Task<OperationResult<Purchase>> AddAsync(
            Purchase purchase,
            object? transaction = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<Purchase>.Success(purchase));

        public Task<OperationResult<Purchase>> GetByIdAsync(int purchaseId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<Purchase>.Failure("Not found."));

        public Task<OperationResult<IReadOnlyList<Purchase>>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<Purchase>>.Success([]));

        public Task<OperationResult<IReadOnlyList<Purchase>>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            GetAllCalled = true;
            return Task.FromResult(OperationResult<IReadOnlyList<Purchase>>.Success(_items));
        }
    }

    // Fake ILogRepository returning configured items for export tests.
    private sealed class FakeLogRepository(IReadOnlyList<LogEntry>? items = null) : ILogRepository
    {
        private readonly IReadOnlyList<LogEntry> _items = items ?? [];
        public bool GetAllCalled { get; private set; }

        public Task<int> AddAsync(LogEntry logEntry, CancellationToken cancellationToken = default) =>
            Task.FromResult(1);

        public Task<IReadOnlyList<LogEntry>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            GetAllCalled = true;
            return Task.FromResult(_items);
        }

        public Task<IReadOnlyList<LogEntry>> GetByDateRangeAsync(
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LogEntry>>([]);

        public Task<IReadOnlyList<LogEntry>> GetByActionTypeAsync(
            string actionType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LogEntry>>([]);
    }
}