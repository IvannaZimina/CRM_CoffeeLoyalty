using System.Globalization;
using System.Diagnostics;
using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.resources;
using Microsoft.Data.Sqlite;
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
    private readonly SqliteConnectionFactory _connectionFactory;

    public DataTransferService(
        IDrinkRepository drinkRepository,
        ICustomerRepository customerRepository,
        ICardRepository cardRepository,
        IPurchaseRepository purchaseRepository,
        ILogRepository logRepository,
        IAuthService authService,
        ILogService logService,
        SqliteConnectionFactory connectionFactory)
    {
        _drinkRepository = drinkRepository ?? throw new ArgumentNullException(nameof(drinkRepository));
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _cardRepository = cardRepository ?? throw new ArgumentNullException(nameof(cardRepository));
        _purchaseRepository = purchaseRepository ?? throw new ArgumentNullException(nameof(purchaseRepository));
        _logRepository = logRepository ?? throw new ArgumentNullException(nameof(logRepository));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
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
                actionType: LogActionType.ErrorExportInvalidPath,
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
                return await FailExportRepositoryAsync("Drinks", cancellationToken);
            }

            OperationResult<IReadOnlyList<Purchase>> purchasesResult =
                await _purchaseRepository.GetAllAsync(cancellationToken);

            if (!purchasesResult.IsSuccess)
            {
                return await FailExportRepositoryAsync("Purchases", cancellationToken);
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
                actionType: LogActionType.LogExportJson,
                description: $"Exported {exportedCount} records to '{Path.GetFileName(filePath)}' " +
                             $"(Drinks: {payload.Drinks.Count}, Customers: {payload.Customers.Count}, " +
                             $"Cards: {payload.Cards.Count}, Purchases: {payload.Purchases.Count}, Logs: {payload.Logs.Count}).",
                objectId: null,
                cancellationToken: cancellationToken);

            return OperationResult<int>.Success(exportedCount);
        }
        catch (SqliteException ex)
        {
            Trace.TraceWarning("JSON export database: {0}", ex.GetType().Name);
            await _logService.LogErrorAsync(LogObjectType, LogActionType.ErrorExportJson,
                CoreResources.ErrorDatabaseConnection, null, cancellationToken);
            return OperationResult<int>.Failure(CoreResources.ErrorDatabaseConnection, "EXPORT_REPOSITORY_ERROR");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.TraceWarning("JSON export: {0}", ex.GetType().Name);
            string message = CoreResources.ErrorJsonExportFailed;

            await _logService.LogErrorAsync(
                objectType: LogObjectType,
                actionType: LogActionType.ErrorExportJson,
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
        OperationResult<ValidatedImport> result = await ReadValidatedImportAsync(filePath, cancellationToken);
        return result.IsSuccess && result.Value is not null
            ? OperationResult<IReadOnlyList<string>>.Success(result.Value.Warnings)
            : OperationResult<IReadOnlyList<string>>.Failure(
                result.ErrorMessage ?? CoreResources.ErrorJsonImportPersistence, result.ErrorCode);
    }

    // The exact parsed payload and pre-existing customer IDs used for validation.
    private sealed record ValidatedImport(DataTransferPayloadDto Payload,
        IReadOnlyList<string> Warnings, HashSet<int> ExistingCustomerIds);

    private async Task<OperationResult<ValidatedImport>> ReadValidatedImportAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        OperationResult accessCheck = await EnsureManagerAccessAsync(
            nameof(ValidateImportAsync),
            cancellationToken);

        if (!accessCheck.IsSuccess)
        {
            return OperationResult<ValidatedImport>.Failure(
                accessCheck.ErrorMessage ?? "Access denied.",
                accessCheck.ErrorCode);
        }

        OperationResult pathCheck = ValidateTargetFilePath(filePath, requireExistingFile: true);
        if (!pathCheck.IsSuccess)
        {
            await _logService.LogErrorAsync(
                objectType: LogObjectType,
                actionType: LogActionType.ErrorImportValidation,
                description: pathCheck.ErrorMessage ?? "Invalid import file path.",
                objectId: null,
                cancellationToken: cancellationToken);

            return OperationResult<ValidatedImport>.Failure(
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
                    actionType: LogActionType.ErrorImportValidation,
                    description: emptyError,
                    objectId: null,
                    cancellationToken: cancellationToken);

                return OperationResult<ValidatedImport>.Failure(emptyError, "INVALID_JSON");
            }

            DataTransferPayloadDto? payload = JsonSerializer.Deserialize<DataTransferPayloadDto>(
                content,
                SerializerOptions);

            if (payload is null)
            {
                const string nullPayloadError = "JSON payload could not be parsed into a valid transfer object.";

                await _logService.LogErrorAsync(
                    objectType: LogObjectType,
                    actionType: LogActionType.ErrorImportValidation,
                    description: nullPayloadError,
                    objectId: null,
                    cancellationToken: cancellationToken);

                return OperationResult<ValidatedImport>.Failure(nullPayloadError, "INVALID_PAYLOAD");
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
                    actionType: LogActionType.ErrorImportValidation,
                    description: joinedSchemaErrors,
                    objectId: null,
                    cancellationToken: cancellationToken);

                return OperationResult<ValidatedImport>.Failure(joinedSchemaErrors, "SCHEMA_VALIDATION_FAILED");
            }

            OperationResult<IReadOnlyList<Drink>> existingDrinksResult =
                await _drinkRepository.GetAllAsync(cancellationToken);

            if (!existingDrinksResult.IsSuccess)
            {
                string readErr = CoreResources.ErrorJsonImportReadFailed;

                await _logService.LogErrorAsync(
                    objectType: LogObjectType,
                    actionType: LogActionType.ErrorImportValidation,
                    description: readErr,
                    objectId: null,
                    cancellationToken: cancellationToken);

                return OperationResult<ValidatedImport>.Failure(readErr, "REPOSITORY_READ_FAILED");
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
                if (drink is null) { errors.Add("Drinks[{i}]: Record cannot be null."); continue; }

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
                if (customer is null) { errors.Add("Customers[{i}]: Record cannot be null."); continue; }

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
                if (card is null) { errors.Add("Cards[{i}]: Record cannot be null."); continue; }
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
                    actionType: LogActionType.ErrorImportValidation,
                    description: $"Validation failed for '{Path.GetFileName(filePath)}': {combinedErrors}",
                    objectId: null,
                    cancellationToken: cancellationToken);

                return OperationResult<ValidatedImport>.Failure(combinedErrors, "VALIDATION_FAILED");
            }

            return OperationResult<ValidatedImport>.Success(new(payload, warnings.AsReadOnly(), existingCustomerIds));
        }
        catch (SqliteException ex)
        {
            Trace.TraceWarning("JSON import validation database: {0}", ex.GetType().Name);
            await _logService.LogErrorAsync(LogObjectType, LogActionType.ErrorImportValidation,
                CoreResources.ErrorDatabaseConnection, null, cancellationToken);
            return OperationResult<ValidatedImport>.Failure(CoreResources.ErrorDatabaseConnection, "REPOSITORY_READ_FAILED");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.TraceWarning("JSON import validation: {0}", ex.GetType().Name);
            string message = CoreResources.ErrorJsonImportReadFailed;

            await _logService.LogErrorAsync(
                objectType: LogObjectType,
                actionType: LogActionType.ErrorImportValidation,
                description: message,
                objectId: null,
                cancellationToken: cancellationToken);

            return OperationResult<ValidatedImport>.Failure(message, "IMPORT_READ_ERROR");
        }
    }

    // Imports the same validated payload using one mandatory SQLite transaction.
    public async Task<OperationResult<int>> ImportFromJsonAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OperationResult accessCheck = await EnsureManagerAccessAsync(
            nameof(ImportFromJsonAsync), cancellationToken);
        if (accessCheck.IsFailure)
            return OperationResult<int>.Failure(accessCheck.ErrorMessage!, accessCheck.ErrorCode);

        int importedCount = 0;
        DataTransferPayloadDto payload;
        try
        {
            OperationResult<ValidatedImport> validation = await ReadValidatedImportAsync(filePath, cancellationToken);
            if (validation.IsFailure || validation.Value is null)
                return OperationResult<int>.Failure(
                    validation.ErrorMessage ?? CoreResources.ErrorJsonImportPersistence, validation.ErrorCode);

            payload = validation.Value.Payload;
            HashSet<int> existingCustomerIds = validation.Value.ExistingCustomerIds;
            Dictionary<int, int> customerIdMap = [];

            // Disposing an uncommitted transaction rolls back all inserts, including cancellation.
            await using (SqliteConnection connection = _connectionFactory.OpenConnection())
            await using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                foreach (DrinkTransferDto dto in payload.Drinks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Drink drink = new(dto.DrinkName!.Trim(), dto.DrinkPrice!.Value,
                        dto.DrinkType!.Trim(), dto.IsActive ?? true, dto.DrinkImageLink);
                    OperationResult<Drink> added = await _drinkRepository.AddAsync(drink, transaction, cancellationToken);
                    if (added.IsFailure || added.Value is null || added.Value.Id <= 0)
                        throw new InvalidOperationException(added.ErrorMessage);
                    importedCount++;
                }

                foreach (CustomerTransferDto dto in payload.Customers)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Customer customer = new(categoryId: dto.CategoryId!.Value,
                        customerName: dto.CustomerName!.Trim(), bonusBalance: dto.BonusBalance ?? 0,
                        birthDate: dto.BirthDate, isActive: dto.IsActive ?? true,
                        timeStamp: dto.TimeStamp ?? DateTime.UtcNow);
                    int id = await _customerRepository.AddAsync(customer, transaction, cancellationToken);
                    if (id <= 0) throw new InvalidOperationException();
                    if (dto.Id.HasValue) customerIdMap.Add(dto.Id.Value, id);
                    importedCount++;
                }

                // Only resolve references to customers that existed before import or were mapped in this batch.
                // Do not query a separate connection for uncommitted customer rows.
                foreach (CardTransferDto dto in payload.Cards)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int sourceId = dto.CustomerId!.Value;
                    if (!customerIdMap.TryGetValue(sourceId, out int customerId))
                    {
                        if (!existingCustomerIds.Contains(sourceId)) throw new InvalidOperationException();
                        customerId = sourceId;
                    }
                    LoyaltyCard card = new(customerId, dto.IsActive ?? true);
                    int id = await _cardRepository.AddAsync(card, transaction, cancellationToken);
                    if (id <= 0) throw new InvalidOperationException();
                    importedCount++;
                }

                cancellationToken.ThrowIfCancellationRequested();
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await AuditImportAsync(true, LogActionType.ErrorImportCancelled, CoreResources.LogJsonImportCancelled);
            throw;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(ex.GetType().Name);
            await AuditImportAsync(true, LogActionType.ErrorImportJson, CoreResources.ErrorJsonImportPersistence);
            return OperationResult<int>.Failure(CoreResources.ErrorJsonImportPersistence, "IMPORT_PERSISTENCE_ERROR");
        }

        // Log after the transaction closes. Audit failure cannot undo or misreport a committed import.
        await AuditImportAsync(false, LogActionType.LogImportJson, string.Format(CultureInfo.InvariantCulture,
            CoreResources.LogJsonImportCompleted, importedCount, Path.GetFileName(filePath),
            payload.Drinks.Count, payload.Customers.Count, payload.Cards.Count));
        return OperationResult<int>.Success(importedCount);
    }

    private async Task AuditImportAsync(bool error, LogActionType action, string message)
    {
        try
        {
            OperationResult result = error
                ? await _logService.LogErrorAsync(LogObjectType, action, message, null, CancellationToken.None)
                : await _logService.LogInfoAsync(LogObjectType, action, message, null, CancellationToken.None);
            if (result.IsFailure) Trace.TraceWarning(CoreResources.ErrorJsonImportAuditFailed);
        }
        catch (Exception)
        {
            Trace.TraceWarning(CoreResources.ErrorJsonImportAuditFailed);
        }
    }

    private async Task<OperationResult<int>> FailExportRepositoryAsync(
        string sectionName,
        CancellationToken cancellationToken)
    {
        string message = string.Format(CoreResources.ErrorJsonExportRepository, sectionName);

        await _logService.LogErrorAsync(
            objectType: LogObjectType,
            actionType: LogActionType.ErrorExportJson,
            description: message,
            objectId: null,
            cancellationToken: cancellationToken);

        return OperationResult<int>.Failure(message, "EXPORT_REPOSITORY_ERROR");
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
                actionType: LogActionType.ErrorAccessDenied,
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
            return OperationResult.Failure(CoreResources.ErrorJsonFileNotFound, "FILE_NOT_FOUND");
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
