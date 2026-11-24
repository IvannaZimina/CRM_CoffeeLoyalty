using System.Globalization;
using System.Text.Json;
using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Interfaces.Services;
using CoffeeLoyalty.Core.interfaces.services;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.Validation;

namespace CoffeeLoyalty.Core.Services;

// Service class that handles exporting data to JSON, validating import files, and importing records into repositories.
public class DataTransferService : IDataTransferService
{
    // Expected JSON payload schema version supported by this service.
    private const string SupportedPayloadVersion = "1.0";

    // Object category name passed to ILogService for all audit and error entries produced by this service.
    private const string LogObjectType = "DataTransfer";

    // Shared JSON serializer settings: pretty-printed output and case-insensitive property matching on read.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly IDrinkRepository _drinkRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ICardRepository _cardRepository;
    private readonly IPurchaseRepository _purchaseRepository;
    private readonly ILogRepository _logRepository;
    private readonly IAuthService _authService;
    private readonly ILogService _logService;

    public DataTransferService(
        IDrinkRepository drinkRepository,
        ICustomerRepository customerRepository,
        ICardRepository cardRepository,
        IPurchaseRepository purchaseRepository,
        ILogRepository logRepository,
        IAuthService authService,
        ILogService logService)
    {
        _drinkRepository = drinkRepository ?? throw new ArgumentNullException(nameof(drinkRepository));
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _cardRepository = cardRepository ?? throw new ArgumentNullException(nameof(cardRepository));
        _purchaseRepository = purchaseRepository ?? throw new ArgumentNullException(nameof(purchaseRepository));
        _logRepository = logRepository ?? throw new ArgumentNullException(nameof(logRepository));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    // Exports all domain datasets (Drinks, Customers, Cards, Purchases, Logs) to a JSON file at filePath.
    public async Task<OperationResult<int>> ExportToJsonAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        OperationResult accessCheck = await EnsureManagerAccessAsync(
            nameof(ExportToJsonAsync),
            cancellationToken);

        if (!accessCheck.IsSuccess)
        {
            return OperationResult<int>.Failure(
                accessCheck.ErrorMessage ?? "Access denied.",
                accessCheck.ErrorCode);
        }

        OperationResult pathCheck = ValidateTargetFilePath(filePath, requireExistingFile: false);
        if (!pathCheck.IsSuccess)
        {
            await _logService.LogErrorAsync(
                objectType: LogObjectType,
                actionType: "ErrorExportInvalidPath",
                description: pathCheck.ErrorMessage ?? "Invalid export file path.",
                objectId: null,
                cancellationToken: cancellationToken);

            return OperationResult<int>.Failure(
                pathCheck.ErrorMessage ?? "Invalid export file path.",
                pathCheck.ErrorCode);
        }

        try
        {
            OperationResult<IReadOnlyList<Drink>> drinksResult =
                await _drinkRepository.GetAllAsync(cancellationToken);

            if (!drinksResult.IsSuccess)
            {
                return await FailExportRepositoryAsync("Drinks", drinksResult.ErrorMessage, cancellationToken);
            }

            OperationResult<IReadOnlyList<Purchase>> purchasesResult =
                await _purchaseRepository.GetAllAsync(cancellationToken);

            if (!purchasesResult.IsSuccess)
            {
                return await FailExportRepositoryAsync("Purchases", purchasesResult.ErrorMessage, cancellationToken);
            }

            IReadOnlyList<Drink> drinks = drinksResult.Value ?? [];
            IReadOnlyList<Purchase> purchases = purchasesResult.Value ?? [];

            IReadOnlyList<Customer> customers = await _customerRepository.GetAllAsync(cancellationToken);
            IReadOnlyList<LoyaltyCard> cards = await _cardRepository.GetAllAsync(cancellationToken);
            IReadOnlyList<LogEntry> logs = await _logRepository.GetAllAsync(cancellationToken);

            DataTransferPayloadDto payload = new()
            {
                Version = SupportedPayloadVersion,
                ExportedAtUtc = DateTime.UtcNow,
                Drinks = drinks.Select(MapDrinkToDto).ToList(),
                Customers = customers.Select(MapCustomerToDto).ToList(),
                Cards = cards.Select(MapCardToDto).ToList(),
                Purchases = purchases.Select(MapPurchaseToDto).ToList(),
                Logs = logs.Select(MapLogToDto).ToList()
            };

            string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(payload, SerializerOptions);
            await File.WriteAllTextAsync(filePath, json, cancellationToken);

            int exportedCount =
                payload.Drinks.Count +
                payload.Customers.Count +
                payload.Cards.Count +
                payload.Purchases.Count +
                payload.Logs.Count;

            await _logService.LogInfoAsync(
                objectType: LogObjectType,
                actionType: "LogExportJson",
                description: $"Exported {exportedCount} records to '{Path.GetFileName(filePath)}' " +
                             $"(Drinks: {payload.Drinks.Count}, Customers: {payload.Customers.Count}, " +
                             $"Cards: {payload.Cards.Count}, Purchases: {payload.Purchases.Count}, Logs: {payload.Logs.Count}).",
                objectId: null,
                cancellationToken: cancellationToken);

            return OperationResult<int>.Success(exportedCount);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            string message = $"Failed to export JSON data: {ex.Message}";

            await _logService.LogErrorAsync(
                objectType: LogObjectType,
                actionType: "ErrorExportJson",
                description: message,
                objectId: null,
                cancellationToken: cancellationToken);

            return OperationResult<int>.Failure(message, "EXPORT_IO_ERROR");
        }
    }

    // Validates an import JSON file (existence, syntax, schema version, fields, duplicates, references) without writing to DB.
    public async Task<OperationResult<IReadOnlyList<string>>> ValidateImportAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        OperationResult accessCheck = await EnsureManagerAccessAsync(
            nameof(ValidateImportAsync),
            cancellationToken);

        if (!accessCheck.IsSuccess)
        {
            return OperationResult<IReadOnlyList<string>>.Failure(
                accessCheck.ErrorMessage ?? "Access denied.",
                accessCheck.ErrorCode);
        }

        OperationResult pathCheck = ValidateTargetFilePath(filePath, requireExistingFile: true);
        if (!pathCheck.IsSuccess)
        {
            await _logService.LogErrorAsync(
                objectType: LogObjectType,
                actionType: "ErrorImportValidation",
                description: pathCheck.ErrorMessage ?? "Invalid import file path.",
                objectId: null,
                cancellationToken: cancellationToken);

            return OperationResult<IReadOnlyList<string>>.Failure(
                pathCheck.ErrorMessage ?? "Invalid import file path.",
                pathCheck.ErrorCode);
        }

        try
        {
            string content = await File.ReadAllTextAsync(filePath, cancellationToken);
            if (!ValidationHelper.HasText(content))
            {
                const string emptyError = "Import JSON file is empty.";

                await _logService.LogErrorAsync(
                    objectType: LogObjectType,
                    actionType: "ErrorImportValidation",
                    description: emptyError,
                    objectId: null,
                    cancellationToken: cancellationToken);

                return OperationResult<IReadOnlyList<string>>.Failure(emptyError, "INVALID_JSON");
            }

            DataTransferPayloadDto? payload = JsonSerializer.Deserialize<DataTransferPayloadDto>(
                content,
                SerializerOptions);

            if (payload is null)
            {
                const string nullPayloadError = "JSON payload could not be parsed into a valid transfer object.";

                await _logService.LogErrorAsync(
                    objectType: LogObjectType,
                    actionType: "ErrorImportValidation",
                    description: nullPayloadError,
                    objectId: null,
                    cancellationToken: cancellationToken);

                return OperationResult<IReadOnlyList<string>>.Failure(nullPayloadError, "INVALID_PAYLOAD");
            }

            List<string> errors = [];
            List<string> warnings = [];

            if (!string.Equals(payload.Version, SupportedPayloadVersion, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Unsupported payload version '{payload.Version ?? "<null>"}'. Expected '{SupportedPayloadVersion}'.");
            }

            List<DrinkTransferDto>? drinksList = payload.Drinks;
            List<CustomerTransferDto>? customersList = payload.Customers;
            List<CardTransferDto>? cardsList = payload.Cards;

            if (drinksList is null || customersList is null || cardsList is null)
            {
                errors.Add("Payload must contain Drinks, Customers, and Cards collections.");
            }

            if (errors.Count > 0)
            {
                string joinedSchemaErrors = string.Join(" ", errors);

                await _logService.LogErrorAsync(
                    objectType: LogObjectType,
                    actionType: "ErrorImportValidation",
                    description: joinedSchemaErrors,
                    objectId: null,
                    cancellationToken: cancellationToken);

                return OperationResult<IReadOnlyList<string>>.Failure(joinedSchemaErrors, "SCHEMA_VALIDATION_FAILED");
            }

            OperationResult<IReadOnlyList<Drink>> existingDrinksResult =
                await _drinkRepository.GetAllAsync(cancellationToken);

            if (!existingDrinksResult.IsSuccess)
            {
                string readErr = existingDrinksResult.ErrorMessage ?? "Failed to load existing drinks for validation.";

                await _logService.LogErrorAsync(
                    objectType: LogObjectType,
                    actionType: "ErrorImportValidation",
                    description: readErr,
                    objectId: null,
                    cancellationToken: cancellationToken);

                return OperationResult<IReadOnlyList<string>>.Failure(readErr, "REPOSITORY_READ_FAILED");
            }

            IReadOnlyList<Drink> existingDrinks = existingDrinksResult.Value ?? [];
            IReadOnlyList<Customer> existingCustomers = await _customerRepository.GetAllAsync(cancellationToken);
            IReadOnlyList<LoyaltyCard> existingCards = await _cardRepository.GetAllAsync(cancellationToken);

            HashSet<string> existingDrinkNames = existingDrinks
                .Select(d => d.DrinkName.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            HashSet<int> existingCustomerIds = existingCustomers
                .Select(c => c.Id)
                .ToHashSet();

            HashSet<int> existingCardIds = existingCards
                .Select(c => c.Id)
                .ToHashSet();

            HashSet<string> batchDrinkNames = new(StringComparer.OrdinalIgnoreCase);
            HashSet<int> batchCustomerIds = [];
            HashSet<int> batchCardIds = [];

            for (int i = 0; i < drinksList!.Count; i++)
            {
                DrinkTransferDto drink = drinksList[i];

                if (!ValidationHelper.HasText(drink.DrinkName))
                {
                    errors.Add($"Drinks[{i}]: DrinkName is required.");
                }
                else
                {
                    string normalizedName = drink.DrinkName!.Trim();

                    if (!batchDrinkNames.Add(normalizedName))
                    {
                        errors.Add($"Drinks[{i}]: Duplicate DrinkName '{normalizedName}' inside import file.");
                    }

                    if (existingDrinkNames.Contains(normalizedName))
                    {
                        errors.Add($"Drinks[{i}]: Drink '{normalizedName}' already exists in database.");
                    }
                }

                if (!drink.DrinkPrice.HasValue || !ValidationHelper.IsPositive(drink.DrinkPrice.Value))
                {
                    errors.Add($"Drinks[{i}]: DrinkPrice must be greater than zero.");
                }

                if (!ValidationHelper.HasText(drink.DrinkType))
                {
                    errors.Add($"Drinks[{i}]: DrinkType is required.");
                }
            }

            for (int i = 0; i < customersList!.Count; i++)
            {
                CustomerTransferDto customer = customersList[i];

                if (!ValidationHelper.HasText(customer.CustomerName))
                {
                    errors.Add($"Customers[{i}]: CustomerName is required.");
                }

                if (!customer.CategoryId.HasValue || !ValidationHelper.IsPositive(customer.CategoryId.Value))
                {
                    errors.Add($"Customers[{i}]: CategoryId must be a positive integer.");
                }

                if (customer.BonusBalance.HasValue && !ValidationHelper.IsNonNegative(customer.BonusBalance.Value))
                {
                    errors.Add($"Customers[{i}]: BonusBalance cannot be negative.");
                }

                if (customer.Id.HasValue)
                {
                    if (!ValidationHelper.IsPositive(customer.Id.Value))
                    {
                        errors.Add($"Customers[{i}]: Id must be positive when specified.");
                    }
                    else
                    {
                        if (!batchCustomerIds.Add(customer.Id.Value))
                        {
                            errors.Add($"Customers[{i}]: Duplicate Id {customer.Id.Value} inside import file.");
                        }

                        if (existingCustomerIds.Contains(customer.Id.Value))
                        {
                            errors.Add($"Customers[{i}]: Customer with Id {customer.Id.Value} already exists.");
                        }
                    }
                }
            }

            for (int i = 0; i < cardsList!.Count; i++)
            {
                CardTransferDto card = cardsList[i];
                bool hasValidCustomerRef = false;

                if (card.CustomerId.HasValue && ValidationHelper.IsPositive(card.CustomerId.Value))
                {
                    if (existingCustomerIds.Contains(card.CustomerId.Value) || batchCustomerIds.Contains(card.CustomerId.Value))
                    {
                        hasValidCustomerRef = true;
                    }
                }

                if (!hasValidCustomerRef)
                {
                    errors.Add($"Cards[{i}]: CustomerId {card.CustomerId?.ToString(CultureInfo.InvariantCulture) ?? "<null>"} does not reference a valid existing or imported customer.");
                }

                if (card.Id.HasValue)
                {
                    if (!ValidationHelper.IsPositive(card.Id.Value))
                    {
                        errors.Add($"Cards[{i}]: Id must be positive when specified.");
                    }
                    else
                    {
                        if (!batchCardIds.Add(card.Id.Value))
                        {
                            errors.Add($"Cards[{i}]: Duplicate Id {card.Id.Value} inside import file.");
                        }

                        if (existingCardIds.Contains(card.Id.Value))
                        {
                            errors.Add($"Cards[{i}]: Card with Id {card.Id.Value} already exists.");
                        }
                    }
                }
            }

            if ((payload.Purchases?.Count ?? 0) > 0 || (payload.Logs?.Count ?? 0) > 0)
            {
                warnings.Add("Purchases and Logs sections in import files are ignored to preserve receipt and audit history.");
            }

            if (errors.Count > 0)
            {
                string combinedErrors = string.Join(" ", errors);

                await _logService.LogErrorAsync(
                    objectType: LogObjectType,
                    actionType: "ErrorImportValidation",
                    description: $"Validation failed for '{Path.GetFileName(filePath)}': {combinedErrors}",
                    objectId: null,
                    cancellationToken: cancellationToken);

                return OperationResult<IReadOnlyList<string>>.Failure(combinedErrors, "VALIDATION_FAILED");
            }

            return OperationResult<IReadOnlyList<string>>.Success(warnings.AsReadOnly());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            string message = $"Failed to read or parse import JSON file: {ex.Message}";

            await _logService.LogErrorAsync(
                objectType: LogObjectType,
                actionType: "ErrorImportValidation",
                description: message,
                objectId: null,
                cancellationToken: cancellationToken);

            return OperationResult<IReadOnlyList<string>>.Failure(message, "IMPORT_READ_ERROR");
        }
    }

    // Validates and imports drinks, customers, and loyalty cards from a JSON file, rolling back created records on failure.
    public async Task<OperationResult<int>> ImportFromJsonAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        OperationResult accessCheck = await EnsureManagerAccessAsync(
            nameof(ImportFromJsonAsync),
            cancellationToken);

        if (!accessCheck.IsSuccess)
        {
            return OperationResult<int>.Failure(
                accessCheck.ErrorMessage ?? "Access denied.",
                accessCheck.ErrorCode);
        }

        OperationResult<IReadOnlyList<string>> validationResult = await ValidateImportAsync(
            filePath,
            cancellationToken);

        if (!validationResult.IsSuccess)
        {
            return OperationResult<int>.Failure(
                validationResult.ErrorMessage ?? "Import validation failed.",
                validationResult.ErrorCode);
        }

        List<int> createdDrinkIds = [];
        List<int> createdCustomerIds = [];
        List<int> createdCardIds = [];

        try
        {
            string content = await File.ReadAllTextAsync(filePath, cancellationToken);
            DataTransferPayloadDto? payload = JsonSerializer.Deserialize<DataTransferPayloadDto>(
                content,
                SerializerOptions);

            if (payload is null || payload.Drinks is null || payload.Customers is null || payload.Cards is null)
            {
                return OperationResult<int>.Failure("Invalid JSON payload.", "INVALID_PAYLOAD");
            }

            int importedCount = 0;
            Dictionary<int, int> customerIdMap = [];

            foreach (DrinkTransferDto dto in payload.Drinks)
            {
                Drink drink = new(
                    drinkName: dto.DrinkName!.Trim(),
                    drinkPrice: dto.DrinkPrice!.Value,
                    drinkType: dto.DrinkType!.Trim(),
                    isActive: dto.IsActive ?? true,
                    drinkImageLink: dto.DrinkImageLink);

                OperationResult<Drink> addDrinkResult = await _drinkRepository.AddAsync(drink, cancellationToken);
                if (!addDrinkResult.IsSuccess || addDrinkResult.Value is null)
                {
                    await RollbackImportedRecordsAsync(createdDrinkIds, createdCustomerIds, createdCardIds);
                    return await FailImportPersistenceAsync(
                        addDrinkResult.ErrorMessage ?? "Failed to import drink.",
                        cancellationToken);
                }

                if (addDrinkResult.Value.Id > 0)
                {
                    createdDrinkIds.Add(addDrinkResult.Value.Id);
                }

                importedCount++;
            }

            foreach (CustomerTransferDto dto in payload.Customers)
            {
                Customer customer = new(
                    categoryId: dto.CategoryId!.Value,
                    customerName: dto.CustomerName!.Trim(),
                    bonusBalance: dto.BonusBalance ?? 0,
                    birthDate: dto.BirthDate,
                    isActive: dto.IsActive ?? true,
                    timeStamp: dto.TimeStamp ?? DateTime.UtcNow);

                int createdCustomerId = await _customerRepository.AddAsync(customer, cancellationToken);
                if (createdCustomerId <= 0)
                {
                    await RollbackImportedRecordsAsync(createdDrinkIds, createdCustomerIds, createdCardIds);
                    return await FailImportPersistenceAsync(
                        $"Failed to import customer '{customer.CustomerName}'.",
                        cancellationToken);
                }

                createdCustomerIds.Add(createdCustomerId);

                if (dto.Id.HasValue)
                {
                    customerIdMap[dto.Id.Value] = createdCustomerId;
                }

                importedCount++;
            }

            IReadOnlyList<Customer> allCustomers = await _customerRepository.GetAllAsync(cancellationToken);
            HashSet<int> validCustomerIds = allCustomers.Select(c => c.Id).ToHashSet();

            foreach (CardTransferDto dto in payload.Cards)
            {
                int rawCustomerId = dto.CustomerId!.Value;

                int resolvedCustomerId = customerIdMap.TryGetValue(rawCustomerId, out int mappedId)
                    ? mappedId
                    : rawCustomerId;

                if (!validCustomerIds.Contains(resolvedCustomerId))
                {
                    await RollbackImportedRecordsAsync(createdDrinkIds, createdCustomerIds, createdCardIds);

                    string refError = $"Cannot import card because referenced CustomerId {rawCustomerId} does not exist.";

                    await _logService.LogErrorAsync(
                        objectType: LogObjectType,
                        actionType: "ErrorImportJson",
                        description: refError,
                        objectId: null,
                        cancellationToken: cancellationToken);

                    return OperationResult<int>.Failure(refError, "INVALID_CUSTOMER_REFERENCE");
                }

                LoyaltyCard card = new(
                    customerId: resolvedCustomerId,
                    isActive: dto.IsActive ?? true);

                int createdCardId = await _cardRepository.AddAsync(card, cancellationToken);
                if (createdCardId <= 0)
                {
                    await RollbackImportedRecordsAsync(createdDrinkIds, createdCustomerIds, createdCardIds);
                    return await FailImportPersistenceAsync(
                        $"Failed to import loyalty card for CustomerId {resolvedCustomerId}.",
                        cancellationToken);
                }

                createdCardIds.Add(createdCardId);
                importedCount++;
            }

            await _logService.LogInfoAsync(
                objectType: LogObjectType,
                actionType: "LogImportJson",
                description: $"Imported {importedCount} records from '{Path.GetFileName(filePath)}' " +
                             $"(Drinks: {payload.Drinks.Count}, Customers: {payload.Customers.Count}, Cards: {payload.Cards.Count}).",
                objectId: null,
                cancellationToken: cancellationToken);

            return OperationResult<int>.Success(importedCount);
        }
        catch (Exception ex)
        {
            await RollbackImportedRecordsAsync(createdDrinkIds, createdCustomerIds, createdCardIds);

            return await FailImportPersistenceAsync(
                $"Import failed during persistence: {ex.Message}",
                cancellationToken);
        }
    }

    // Best-effort compensation: deactivates records added during a failed import so they do not remain active.
    private async Task RollbackImportedRecordsAsync(
        IReadOnlyList<int> drinkIds,
        IReadOnlyList<int> customerIds,
        IReadOnlyList<int> cardIds)
    {
        foreach (int cardId in cardIds)
        {
            try
            {
                await _cardRepository.SetActiveStatusAsync(cardId, false, CancellationToken.None);
            }
            catch
            {
                // Ignore secondary rollback failures so the primary error is preserved.
            }
        }

        foreach (int customerId in customerIds)
        {
            try
            {
                await _customerRepository.SetActiveStatusAsync(customerId, false, CancellationToken.None);
            }
            catch
            {
                // Ignore secondary rollback failures so the primary error is preserved.
            }
        }

        foreach (int drinkId in drinkIds)
        {
            try
            {
                await _drinkRepository.SetActiveStatusAsync(drinkId, false, CancellationToken.None);
            }
            catch
            {
                // Ignore secondary rollback failures so the primary error is preserved.
            }
        }
    }

    private async Task<OperationResult<int>> FailExportRepositoryAsync(
        string sectionName,
        string? details,
        CancellationToken cancellationToken)
    {
        string message = $"Failed to load {sectionName} for export: {details ?? "Repository error."}";

        await _logService.LogErrorAsync(
            objectType: LogObjectType,
            actionType: "ErrorExportJson",
            description: message,
            objectId: null,
            cancellationToken: cancellationToken);

        return OperationResult<int>.Failure(message, "EXPORT_REPOSITORY_ERROR");
    }

    private async Task<OperationResult<int>> FailImportPersistenceAsync(
        string message,
        CancellationToken cancellationToken)
    {
        await _logService.LogErrorAsync(
            objectType: LogObjectType,
            actionType: "ErrorImportJson",
            description: message,
            objectId: null,
            cancellationToken: cancellationToken);

        return OperationResult<int>.Failure(message, "IMPORT_PERSISTENCE_ERROR");
    }

    private async Task<OperationResult> EnsureManagerAccessAsync(
        string operationName,
        CancellationToken cancellationToken)
    {
        if (!_authService.IsAuthenticated || !_authService.HasPermission(UserRole.Manager))
        {
            string currentRoleText = _authService.CurrentRole?.ToString() ?? "Unauthenticated";

            await _logService.LogErrorAsync(
                objectType: LogObjectType,
                actionType: "ErrorAccessDenied",
                description: $"Access denied for {operationName}. Required role: {UserRole.Manager}, current: {currentRoleText}.",
                objectId: null,
                cancellationToken: cancellationToken);

            return OperationResult.Failure(
                "Access denied. Only an authenticated Manager can perform data transfer operations.",
                "ACCESS_DENIED");
        }

        return OperationResult.Success();
    }

    private static OperationResult ValidateTargetFilePath(string filePath, bool requireExistingFile)
    {
        if (!ValidationHelper.HasText(filePath))
        {
            return OperationResult.Failure("File path cannot be empty.", "INVALID_FILE_PATH");
        }

        if (filePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return OperationResult.Failure("File path contains invalid characters.", "INVALID_FILE_PATH");
        }

        string extension = Path.GetExtension(filePath);
        if (!string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Failure("File must have a .json extension.", "INVALID_FILE_EXTENSION");
        }

        if (requireExistingFile && !File.Exists(filePath))
        {
            return OperationResult.Failure($"File '{filePath}' does not exist.", "FILE_NOT_FOUND");
        }

        return OperationResult.Success();
    }

    private static DrinkTransferDto MapDrinkToDto(Drink drink) => new()
    {
        Id = drink.Id,
        DrinkName = drink.DrinkName,
        DrinkPrice = drink.DrinkPrice,
        DrinkType = drink.DrinkType,
        IsActive = drink.IsActive,
        DrinkImageLink = drink.DrinkImageLink
    };

    private static CustomerTransferDto MapCustomerToDto(Customer customer) => new()
    {
        Id = customer.Id,
        CategoryId = customer.CategoryId,
        CustomerName = customer.CustomerName,
        BonusBalance = customer.BonusBalance,
        BirthDate = customer.BirthDate,
        IsActive = customer.IsActive,
        TimeStamp = customer.TimeStamp
    };

    private static CardTransferDto MapCardToDto(LoyaltyCard card) => new()
    {
        Id = card.Id,
        CustomerId = card.CustomerId,
        IsActive = card.IsActive
    };

    private static PurchaseTransferDto MapPurchaseToDto(Purchase purchase) => new()
    {
        Id = purchase.Id,
        CardId = purchase.CardId,
        EmployeeId = purchase.EmployeeId,
        TotalAmount = purchase.TotalAmount,
        BonusesSpent = purchase.BonusesSpent,
        BonusesEarned = purchase.BonusesEarned,
        TimeStamp = purchase.TimeStamp
    };

    private static LogTransferDto MapLogToDto(LogEntry log) => new()
    {
        Id = log.Id,
        ObjectId = log.ObjectId,
        EmployeeId = log.EmployeeId,
        ObjectType = log.ObjectType,
        ActionType = log.ActionType,
        Description = log.Description,
        TimeStamp = log.TimeStamp
    };
}