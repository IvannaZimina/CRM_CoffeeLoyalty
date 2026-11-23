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

    // Repository for reading and adding drink records.
    private readonly IDrinkRepository _drinkRepository;

    // Repository for reading and adding customer profiles.
    private readonly ICustomerRepository _customerRepository;

    // Repository for reading and adding loyalty cards.
    private readonly ICardRepository _cardRepository;

    // Repository for reading purchase history during JSON export.
    private readonly IPurchaseRepository _purchaseRepository;

    // Repository for reading log entries during JSON export.
    private readonly ILogRepository _logRepository;

    // Authentication service used to check if a Manager session is active and get the current employee ID.
    private readonly IAuthService _authService;

    // Logging service used to write informational and error events to the application log.
    private readonly ILogService _logService;

    // Constructor: receives all required repositories and services and validates that none of them are null.
    public DataTransferService(
        IDrinkRepository drinkRepository,
        ICustomerRepository customerRepository,
        ICardRepository cardRepository,
        IPurchaseRepository purchaseRepository,
        ILogRepository logRepository,
        IAuthService authService,
        ILogService logService)
    {
        // Ensure drinkRepository is provided; throw ArgumentNullException immediately if missing.
        _drinkRepository = drinkRepository ?? throw new ArgumentNullException(nameof(drinkRepository));

        // Ensure customerRepository is provided; throw ArgumentNullException immediately if missing.
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));

        // Ensure cardRepository is provided; throw ArgumentNullException immediately if missing.
        _cardRepository = cardRepository ?? throw new ArgumentNullException(nameof(cardRepository));

        // Ensure purchaseRepository is provided; throw ArgumentNullException immediately if missing.
        _purchaseRepository = purchaseRepository ?? throw new ArgumentNullException(nameof(purchaseRepository));

        // Ensure logRepository is provided; throw ArgumentNullException immediately if missing.
        _logRepository = logRepository ?? throw new ArgumentNullException(nameof(logRepository));

        // Ensure authService is provided; throw ArgumentNullException immediately if missing.
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));

        // Ensure logService is provided; throw ArgumentNullException immediately if missing.
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    // Exports all domain datasets (Drinks, Customers, Cards, Purchases, Logs) to a JSON file at filePath.
    public async Task<OperationResult<int>> ExportToJsonAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Verify that the caller is authenticated and has the UserRole.Manager role.
        OperationResult accessCheck = await EnsureManagerAccessAsync(
            nameof(ExportToJsonAsync),
            cancellationToken);

        // If access check failed, stop immediately and return the denial error without touching files or repositories.
        if (!accessCheck.IsSuccess)
        {
            return OperationResult<int>.Failure(
                accessCheck.ErrorMessage ?? "Access denied.",
                accessCheck.ErrorCode);
        }

        // Step 2: Validate that filePath is non-empty, has valid path characters, and ends with .json.
        OperationResult pathCheck = ValidateTargetFilePath(filePath, requireExistingFile: false);
        if (!pathCheck.IsSuccess)
        {
            // Log the invalid file path error together with the current employee ID (if logged in).
            await _logService.LogErrorAsync(
                objectType: LogObjectType,
                actionType: "ErrorExportInvalidPath",
                description: pathCheck.ErrorMessage ?? "Invalid export file path.",
                objectId: _authService.CurrentEmployee?.Id,
                cancellationToken: cancellationToken);

            // Return a failed OperationResult<int> with the path validation message and error code.
            return OperationResult<int>.Failure(
                pathCheck.ErrorMessage ?? "Invalid export file path.",
                pathCheck.ErrorCode);
        }

        try
        {
            // Step 3a: Fetch all drinks from IDrinkRepository (returns OperationResult<IReadOnlyList<Drink>>).
            OperationResult<IReadOnlyList<Drink>> drinksResult =
                await _drinkRepository.GetAllAsync(cancellationToken);

            // If reading drinks failed, log the error and return a failed export result.
            if (!drinksResult.IsSuccess)
            {
                return await FailExportRepositoryAsync("Drinks", drinksResult.ErrorMessage, cancellationToken);
            }

            // Step 3b: Fetch all purchases from IPurchaseRepository (returns OperationResult<IReadOnlyList<Purchase>>).
            OperationResult<IReadOnlyList<Purchase>> purchasesResult =
                await _purchaseRepository.GetAllAsync(cancellationToken);

            // If reading purchases failed, log the error and return a failed export result.
            if (!purchasesResult.IsSuccess)
            {
                return await FailExportRepositoryAsync("Purchases", purchasesResult.ErrorMessage, cancellationToken);
            }

            // Unwrap lists from OperationResult (falling back to empty list if Value is null).
            IReadOnlyList<Drink> drinks = drinksResult.Value ?? [];
            IReadOnlyList<Purchase> purchases = purchasesResult.Value ?? [];

            // Step 4: Fetch customers, loyalty cards, and logs (these repositories return lists directly).
            IReadOnlyList<Customer> customers = await _customerRepository.GetAllAsync(cancellationToken);
            IReadOnlyList<LoyaltyCard> cards = await _cardRepository.GetAllAsync(cancellationToken);
            IReadOnlyList<LogEntry> logs = await _logRepository.GetAllAsync(cancellationToken);

            // Step 5: Assemble the root DataTransferPayloadDto and map each domain entity to its transfer DTO.
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

            // Step 6: Create the target directory if it does not already exist on disk.
            string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Step 7: Serialize payload into formatted JSON text and write it asynchronously to filePath.
            string json = JsonSerializer.Serialize(payload, SerializerOptions);
            await File.WriteAllTextAsync(filePath, json, cancellationToken);

            // Step 8: Calculate the total number of exported records across all five collections.
            int exportedCount =
                payload.Drinks.Count +
                payload.Customers.Count +
                payload.Cards.Count +
                payload.Purchases.Count +
                payload.Logs.Count;

            // Step 9: Write an informational log entry recording exported record counts, file name, and employee ID.
            await _logService.LogInfoAsync(
                objectType: LogObjectType,
                actionType: "LogExportJson",
                description: $"Exported {exportedCount} records to '{Path.GetFileName(filePath)}' " +
                             $"(Drinks: {payload.Drinks.Count}, Customers: {payload.Customers.Count}, " +
                             $"Cards: {payload.Cards.Count}, Purchases: {payload.Purchases.Count}, Logs: {payload.Logs.Count}).",
                objectId: _authService.CurrentEmployee?.Id,
                cancellationToken: cancellationToken);

            // Step 10: Return success with the total exported record count.
            return OperationResult<int>.Success(exportedCount);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Step 11: Catch file I/O, permission, or JSON serialization exceptions so the UI never crashes.
            string message = $"Failed to export JSON data: {ex.Message}";

            // Log the I/O or serialization failure via ILogService.
            await _logService.LogErrorAsync(
                objectType: LogObjectType,
                actionType: "ErrorExportJson",
                description: message,
                objectId: _authService.CurrentEmployee?.Id,
                cancellationToken: cancellationToken);

            // Return a structured failure result with error code EXPORT_IO_ERROR.
            return OperationResult<int>.Failure(message, "EXPORT_IO_ERROR");
        }
    }

    // Validates an import JSON file (existence, syntax, schema version, fields, duplicates, references) without writing to DB.
    public async Task<OperationResult<IReadOnlyList<string>>> ValidateImportAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Verify that an authenticated Manager session is active before reading the file.
        OperationResult accessCheck = await EnsureManagerAccessAsync(
            nameof(ValidateImportAsync),
            cancellationToken);

        // If access check failed, return the denial error immediately.
        if (!accessCheck.IsSuccess)
        {
            return OperationResult<IReadOnlyList<string>>.Failure(
                accessCheck.ErrorMessage ?? "Access denied.",
                accessCheck.ErrorCode);
        }

        // Step 2: Check that filePath is valid and that the file exists on disk.
        OperationResult pathCheck = ValidateTargetFilePath(filePath, requireExistingFile: true);
        if (!pathCheck.IsSuccess)
        {
            // Log validation error when the file path is invalid or missing.
            await _logService.LogErrorAsync(
                objectType: LogObjectType,
                actionType: "ErrorImportValidation",
                description: pathCheck.ErrorMessage ?? "Invalid import file path.",
                objectId: _authService.CurrentEmployee?.Id,
                cancellationToken: cancellationToken);

            return OperationResult<IReadOnlyList<string>>.Failure(
                pathCheck.ErrorMessage ?? "Invalid import file path.",
                pathCheck.ErrorCode);
        }

        try
        {
            // Step 3: Read the file content and ensure it is not empty or whitespace-only.
            string content = await File.ReadAllTextAsync(filePath, cancellationToken);
            if (!ValidationHelper.HasText(content))
            {
                const string emptyError = "Import JSON file is empty.";

                // Log empty file error via ILogService.
                await _logService.LogErrorAsync(
                    objectType: LogObjectType,
                    actionType: "ErrorImportValidation",
                    description: emptyError,
                    objectId: _authService.CurrentEmployee?.Id,
                    cancellationToken: cancellationToken);

                return OperationResult<IReadOnlyList<string>>.Failure(emptyError, "INVALID_JSON");
            }

            // Step 4: Deserialize JSON text into DataTransferPayloadDto.
            DataTransferPayloadDto? payload = JsonSerializer.Deserialize<DataTransferPayloadDto>(
                content,
                SerializerOptions);

            // If deserialization produced null (for example JSON literal "null"), return an error.
            if (payload is null)
            {
                const string nullPayloadError = "JSON payload could not be parsed into a valid transfer object.";

                // Log null payload error via ILogService.
                await _logService.LogErrorAsync(
                    objectType: LogObjectType,
                    actionType: "ErrorImportValidation",
                    description: nullPayloadError,
                    objectId: _authService.CurrentEmployee?.Id,
                    cancellationToken: cancellationToken);

                return OperationResult<IReadOnlyList<string>>.Failure(nullPayloadError, "INVALID_PAYLOAD");
            }

            // Accumulate all validation errors and non-blocking warnings across checks.
            List<string> errors = [];
            List<string> warnings = [];

            // Step 5a: Verify that the payload version matches SupportedPayloadVersion ("1.0").
            if (!string.Equals(payload.Version, SupportedPayloadVersion, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Unsupported payload version '{payload.Version ?? "<null>"}'. Expected '{SupportedPayloadVersion}'.");
            }

            // Step 5b: Extract required collections and verify none of them are null.
            List<DrinkTransferDto>? drinksList = payload.Drinks;
            List<CustomerTransferDto>? customersList = payload.Customers;
            List<CardTransferDto>? cardsList = payload.Cards;

            if (drinksList is null || customersList is null || cardsList is null)
            {
                errors.Add("Payload must contain Drinks, Customers, and Cards collections.");
            }

            // If basic schema checks failed, log and return before querying repositories.
            if (errors.Count > 0)
            {
                string joinedSchemaErrors = string.Join(" ", errors);

                await _logService.LogErrorAsync(
                    objectType: LogObjectType,
                    actionType: "ErrorImportValidation",
                    description: joinedSchemaErrors,
                    objectId: _authService.CurrentEmployee?.Id,
                    cancellationToken: cancellationToken);

                return OperationResult<IReadOnlyList<string>>.Failure(joinedSchemaErrors, "SCHEMA_VALIDATION_FAILED");
            }

            // Step 6a: Load existing drinks from repository to check for duplicate drink names.
            OperationResult<IReadOnlyList<Drink>> existingDrinksResult =
                await _drinkRepository.GetAllAsync(cancellationToken);

            if (!existingDrinksResult.IsSuccess)
            {
                string readErr = existingDrinksResult.ErrorMessage ?? "Failed to load existing drinks for validation.";

                await _logService.LogErrorAsync(
                    objectType: LogObjectType,
                    actionType: "ErrorImportValidation",
                    description: readErr,
                    objectId: _authService.CurrentEmployee?.Id,
                    cancellationToken: cancellationToken);

                return OperationResult<IReadOnlyList<string>>.Failure(readErr, "REPOSITORY_READ_FAILED");
            }

            // Step 6b: Load existing customers and loyalty cards to check ID collisions and customer references.
            IReadOnlyList<Drink> existingDrinks = existingDrinksResult.Value ?? [];
            IReadOnlyList<Customer> existingCustomers = await _customerRepository.GetAllAsync(cancellationToken);
            IReadOnlyList<LoyaltyCard> existingCards = await _cardRepository.GetAllAsync(cancellationToken);

            // Build lookup sets of existing drink names (case-insensitive) and existing customer/card IDs.
            HashSet<string> existingDrinkNames = existingDrinks
                .Select(d => d.DrinkName.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            HashSet<int> existingCustomerIds = existingCustomers
                .Select(c => c.Id)
                .ToHashSet();

            HashSet<int> existingCardIds = existingCards
                .Select(c => c.Id)
                .ToHashSet();

            // Build lookup sets to detect duplicate drink names or duplicate IDs within the import file itself.
            HashSet<string> batchDrinkNames = new(StringComparer.OrdinalIgnoreCase);
            HashSet<int> batchCustomerIds = [];
            HashSet<int> batchCardIds = [];

            // Step 7: Validate each drink item (DrinkName, DrinkPrice > 0, DrinkType, and no duplicate names).
            for (int i = 0; i < drinksList!.Count; i++)
            {
                DrinkTransferDto drink = drinksList[i];

                // Check that drink name is non-empty and unique in both the import batch and the database.
                if (!ValidationHelper.HasText(drink.DrinkName))
                {
                    errors.Add($"Drinks[{i}]: DrinkName is required.");
                }
                else
                {
                    string normalizedName = drink.DrinkName!.Trim();

                    // Check for duplicate drink name within the same import file.
                    if (!batchDrinkNames.Add(normalizedName))
                    {
                        errors.Add($"Drinks[{i}]: Duplicate DrinkName '{normalizedName}' inside import file.");
                    }

                    // Check if a drink with this name already exists in the database.
                    if (existingDrinkNames.Contains(normalizedName))
                    {
                        errors.Add($"Drinks[{i}]: Drink '{normalizedName}' already exists in database.");
                    }
                }

                // Check that price is specified and strictly positive.
                if (!drink.DrinkPrice.HasValue || !ValidationHelper.IsPositive(drink.DrinkPrice.Value))
                {
                    errors.Add($"Drinks[{i}]: DrinkPrice must be greater than zero.");
                }

                // Check that drink type is non-empty.
                if (!ValidationHelper.HasText(drink.DrinkType))
                {
                    errors.Add($"Drinks[{i}]: DrinkType is required.");
                }
            }

            // Step 8: Validate each customer item (CustomerName, CategoryId > 0, BonusBalance >= 0, unique Id).
            for (int i = 0; i < customersList!.Count; i++)
            {
                CustomerTransferDto customer = customersList[i];

                // Customer name must not be empty or whitespace.
                if (!ValidationHelper.HasText(customer.CustomerName))
                {
                    errors.Add($"Customers[{i}]: CustomerName is required.");
                }

                // CategoryId must be present and positive.
                if (!customer.CategoryId.HasValue || !ValidationHelper.IsPositive(customer.CategoryId.Value))
                {
                    errors.Add($"Customers[{i}]: CategoryId must be a positive integer.");
                }

                // BonusBalance, if provided, must not be negative.
                if (customer.BonusBalance.HasValue && !ValidationHelper.IsNonNegative(customer.BonusBalance.Value))
                {
                    errors.Add($"Customers[{i}]: BonusBalance cannot be negative.");
                }

                // If an explicit source Id is provided on the customer DTO, verify it is positive and unique.
                if (customer.Id.HasValue)
                {
                    if (!ValidationHelper.IsPositive(customer.Id.Value))
                    {
                        errors.Add($"Customers[{i}]: Id must be positive when specified.");
                    }
                    else
                    {
                        // Reject duplicate customer IDs inside the import file.
                        if (!batchCustomerIds.Add(customer.Id.Value))
                        {
                            errors.Add($"Customers[{i}]: Duplicate Id {customer.Id.Value} inside import file.");
                        }

                        // Reject customer IDs that already exist in the database.
                        if (existingCustomerIds.Contains(customer.Id.Value))
                        {
                            errors.Add($"Customers[{i}]: Customer with Id {customer.Id.Value} already exists.");
                        }
                    }
                }
            }

            // Step 9: Validate each loyalty card item (CustomerId reference and unique card Id).
            for (int i = 0; i < cardsList!.Count; i++)
            {
                CardTransferDto card = cardsList[i];
                bool hasValidCustomerRef = false;

                // Card's CustomerId is valid if it references an existing customer or a customer being imported in this file.
                if (card.CustomerId.HasValue && ValidationHelper.IsPositive(card.CustomerId.Value))
                {
                    if (existingCustomerIds.Contains(card.CustomerId.Value) || batchCustomerIds.Contains(card.CustomerId.Value))
                    {
                        hasValidCustomerRef = true;
                    }
                }

                // Report an error if the referenced customer does not exist anywhere.
                if (!hasValidCustomerRef)
                {
                    errors.Add($"Cards[{i}]: CustomerId {card.CustomerId?.ToString(CultureInfo.InvariantCulture) ?? "<null>"} does not reference a valid existing or imported customer.");
                }

                // If an explicit card Id is provided, verify it is positive and not duplicated.
                if (card.Id.HasValue)
                {
                    if (!ValidationHelper.IsPositive(card.Id.Value))
                    {
                        errors.Add($"Cards[{i}]: Id must be positive when specified.");
                    }
                    else
                    {
                        // Reject duplicate card IDs inside the import file.
                        if (!batchCardIds.Add(card.Id.Value))
                        {
                            errors.Add($"Cards[{i}]: Duplicate Id {card.Id.Value} inside import file.");
                        }

                        // Reject card IDs that already exist in the database.
                        if (existingCardIds.Contains(card.Id.Value))
                        {
                            errors.Add($"Cards[{i}]: Card with Id {card.Id.Value} already exists.");
                        }
                    }
                }
            }

            // Step 10: Add a non-blocking warning if Purchases or Logs sections are present in the JSON file.
            if ((payload.Purchases?.Count ?? 0) > 0 || (payload.Logs?.Count ?? 0) > 0)
            {
                warnings.Add("Purchases and Logs sections in import files are ignored to preserve receipt and audit history.");
            }

            // Step 11: If there are any validation errors, log them via ILogService and return Failure.
            if (errors.Count > 0)
            {
                string combinedErrors = string.Join(" ", errors);

                await _logService.LogErrorAsync(
                    objectType: LogObjectType,
                    actionType: "ErrorImportValidation",
                    description: $"Validation failed for '{Path.GetFileName(filePath)}': {combinedErrors}",
                    objectId: _authService.CurrentEmployee?.Id,
                    cancellationToken: cancellationToken);

                return OperationResult<IReadOnlyList<string>>.Failure(combinedErrors, "VALIDATION_FAILED");
            }

            // Step 12: Validation passed; return Success along with any warnings.
            return OperationResult<IReadOnlyList<string>>.Success(warnings.AsReadOnly());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Step 13: Handle file read or JSON syntax errors safely, log them, and return Failure.
            string message = $"Failed to read or parse import JSON file: {ex.Message}";

            await _logService.LogErrorAsync(
                objectType: LogObjectType,
                actionType: "ErrorImportValidation",
                description: message,
                objectId: _authService.CurrentEmployee?.Id,
                cancellationToken: cancellationToken);

            return OperationResult<IReadOnlyList<string>>.Failure(message, "IMPORT_READ_ERROR");
        }
    }

    // Validates and imports drinks, customers, and loyalty cards from a JSON file into the repositories.
    public async Task<OperationResult<int>> ImportFromJsonAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Verify that an authenticated Manager session is active before starting import.
        OperationResult accessCheck = await EnsureManagerAccessAsync(
            nameof(ImportFromJsonAsync),
            cancellationToken);

        // Stop immediately if the user is not an authenticated Manager.
        if (!accessCheck.IsSuccess)
        {
            return OperationResult<int>.Failure(
                accessCheck.ErrorMessage ?? "Access denied.",
                accessCheck.ErrorCode);
        }

        // Step 2: Run full validation first so no records are written if the file or data is invalid.
        OperationResult<IReadOnlyList<string>> validationResult = await ValidateImportAsync(
            filePath,
            cancellationToken);

        // If validation failed, propagate the error without modifying any repository.
        if (!validationResult.IsSuccess)
        {
            return OperationResult<int>.Failure(
                validationResult.ErrorMessage ?? "Import validation failed.",
                validationResult.ErrorCode);
        }

        try
        {
            // Step 3: Read and deserialize the validated JSON file into DataTransferPayloadDto.
            string content = await File.ReadAllTextAsync(filePath, cancellationToken);
            DataTransferPayloadDto? payload = JsonSerializer.Deserialize<DataTransferPayloadDto>(
                content,
                SerializerOptions);

            // Guard against null payload or missing core collections.
            if (payload is null || payload.Drinks is null || payload.Customers is null || payload.Cards is null)
            {
                return OperationResult<int>.Failure("Invalid JSON payload.", "INVALID_PAYLOAD");
            }

            // Track total imported records and map source customer IDs (from JSON) to newly generated DB IDs.
            int importedCount = 0;
            Dictionary<int, int> customerIdMap = [];

            // Step 4: Persist all drinks first.
            foreach (DrinkTransferDto dto in payload.Drinks)
            {
                // Construct a new Drink domain model from the validated DTO fields.
                Drink drink = new(
                    drinkName: dto.DrinkName!.Trim(),
                    drinkPrice: dto.DrinkPrice!.Value,
                    drinkType: dto.DrinkType!.Trim(),
                    isActive: dto.IsActive ?? true,
                    drinkImageLink: dto.DrinkImageLink);

                // Add the drink to IDrinkRepository.
                OperationResult<Drink> addDrinkResult = await _drinkRepository.AddAsync(drink, cancellationToken);
                if (!addDrinkResult.IsSuccess)
                {
                    return await FailImportPersistenceAsync(
                        addDrinkResult.ErrorMessage ?? "Failed to import drink.",
                        cancellationToken);
                }

                importedCount++;
            }

            // Step 5: Persist all customers next and record ID mappings (jsonCustomerId -> createdDbCustomerId).
            foreach (CustomerTransferDto dto in payload.Customers)
            {
                // Construct a new Customer domain model from the validated DTO fields.
                Customer customer = new(
                    categoryId: dto.CategoryId!.Value,
                    customerName: dto.CustomerName!.Trim(),
                    bonusBalance: dto.BonusBalance ?? 0,
                    birthDate: dto.BirthDate,
                    isActive: dto.IsActive ?? true,
                    timeStamp: dto.TimeStamp ?? DateTime.UtcNow);

                // Add the customer to ICustomerRepository and get the newly assigned database ID.
                int createdCustomerId = await _customerRepository.AddAsync(customer, cancellationToken);

                // Store the mapping so cards referencing dto.Id can be linked to createdCustomerId.
                if (dto.Id.HasValue && createdCustomerId > 0)
                {
                    customerIdMap[dto.Id.Value] = createdCustomerId;
                }

                importedCount++;
            }

            // Step 6: Load all customers currently in the repository to verify valid target customer IDs.
            IReadOnlyList<Customer> allCustomers = await _customerRepository.GetAllAsync(cancellationToken);
            HashSet<int> validCustomerIds = allCustomers.Select(c => c.Id).ToHashSet();

            // Step 7: Persist loyalty cards last, resolving customer IDs via customerIdMap when needed.
            foreach (CardTransferDto dto in payload.Cards)
            {
                int rawCustomerId = dto.CustomerId!.Value;

                // Use mapped database ID if the customer was imported in this batch; otherwise keep rawCustomerId.
                int resolvedCustomerId = customerIdMap.TryGetValue(rawCustomerId, out int mappedId)
                    ? mappedId
                    : rawCustomerId;

                // Verify that the resolved customer ID exists in validCustomerIds before inserting the card.
                if (!validCustomerIds.Contains(resolvedCustomerId))
                {
                    string refError = $"Cannot import card because referenced CustomerId {rawCustomerId} does not exist.";

                    await _logService.LogErrorAsync(
                        objectType: LogObjectType,
                        actionType: "ErrorImportJson",
                        description: refError,
                        objectId: _authService.CurrentEmployee?.Id,
                        cancellationToken: cancellationToken);

                    return OperationResult<int>.Failure(refError, "INVALID_CUSTOMER_REFERENCE");
                }

                // Construct and add the LoyaltyCard entity.
                LoyaltyCard card = new(
                    customerId: resolvedCustomerId,
                    isActive: dto.IsActive ?? true);

                await _cardRepository.AddAsync(card, cancellationToken);
                importedCount++;
            }

            // Step 8: Write an informational log entry summarizing the completed import and current employee ID.
            await _logService.LogInfoAsync(
                objectType: LogObjectType,
                actionType: "LogImportJson",
                description: $"Imported {importedCount} records from '{Path.GetFileName(filePath)}' " +
                             $"(Drinks: {payload.Drinks.Count}, Customers: {payload.Customers.Count}, Cards: {payload.Cards.Count}).",
                objectId: _authService.CurrentEmployee?.Id,
                cancellationToken: cancellationToken);

            // Step 9: Return success with the total count of imported records.
            return OperationResult<int>.Success(importedCount);
        }
        catch (Exception ex)
        {
            // Step 10: Catch any unexpected exception during persistence, log it, and return a failure result.
            return await FailImportPersistenceAsync(
                $"Import failed during persistence: {ex.Message}",
                cancellationToken);
        }
    }

    // Helper: logs a repository read error during export and returns a failed OperationResult<int>.
    private async Task<OperationResult<int>> FailExportRepositoryAsync(
        string sectionName,
        string? details,
        CancellationToken cancellationToken)
    {
        // Format a clear description naming the section that failed to load.
        string message = $"Failed to load {sectionName} for export: {details ?? "Repository error."}";

        // Log the error via ILogService with the current employee ID.
        await _logService.LogErrorAsync(
            objectType: LogObjectType,
            actionType: "ErrorExportJson",
            description: message,
            objectId: _authService.CurrentEmployee?.Id,
            cancellationToken: cancellationToken);

        return OperationResult<int>.Failure(message, "EXPORT_REPOSITORY_ERROR");
    }

    // Helper: logs a persistence error during import and returns a failed OperationResult<int>.
    private async Task<OperationResult<int>> FailImportPersistenceAsync(
        string message,
        CancellationToken cancellationToken)
    {
        // Log the error via ILogService with the current employee ID.
        await _logService.LogErrorAsync(
            objectType: LogObjectType,
            actionType: "ErrorImportJson",
            description: message,
            objectId: _authService.CurrentEmployee?.Id,
            cancellationToken: cancellationToken);

        return OperationResult<int>.Failure(message, "IMPORT_PERSISTENCE_ERROR");
    }

    // Helper: checks that IsAuthenticated is true and CurrentRole == UserRole.Manager; logs ErrorAccessDenied otherwise.
    private async Task<OperationResult> EnsureManagerAccessAsync(
        string operationName,
        CancellationToken cancellationToken)
    {
        // Reject unauthenticated users or roles other than Manager (for example Cashier).
        if (!_authService.IsAuthenticated || _authService.CurrentRole != UserRole.Manager)
        {
            int? employeeId = _authService.CurrentEmployee?.Id;
            string currentRoleText = _authService.CurrentRole?.ToString() ?? "Unauthenticated";

            // Record the unauthorized attempt in the audit log.
            await _logService.LogErrorAsync(
                objectType: LogObjectType,
                actionType: "ErrorAccessDenied",
                description: $"Access denied for {operationName}. Required role: {UserRole.Manager}, current: {currentRoleText}.",
                objectId: employeeId,
                cancellationToken: cancellationToken);

            return OperationResult.Failure(
                "Access denied. Only an authenticated Manager can perform data transfer operations.",
                "ACCESS_DENIED");
        }

        // Access is granted.
        return OperationResult.Success();
    }

    // Helper: checks that filePath is non-empty, valid, has a .json extension, and optionally exists on disk.
    private static OperationResult ValidateTargetFilePath(string filePath, bool requireExistingFile)
    {
        // Reject null, empty, or whitespace-only paths.
        if (!ValidationHelper.HasText(filePath))
        {
            return OperationResult.Failure("File path cannot be empty.", "INVALID_FILE_PATH");
        }

        // Reject paths containing characters that are invalid in file system paths.
        if (filePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return OperationResult.Failure("File path contains invalid characters.", "INVALID_FILE_PATH");
        }

        // Ensure the file extension is .json (case-insensitive).
        string extension = Path.GetExtension(filePath);
        if (!string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Failure("File must have a .json extension.", "INVALID_FILE_EXTENSION");
        }

        // For import/validation, verify that the file actually exists.
        if (requireExistingFile && !File.Exists(filePath))
        {
            return OperationResult.Failure($"File '{filePath}' does not exist.", "FILE_NOT_FOUND");
        }

        // Path checks passed.
        return OperationResult.Success();
    }

    // Helper: converts a Drink domain entity into a DrinkTransferDto for JSON serialization.
    private static DrinkTransferDto MapDrinkToDto(Drink drink) => new()
    {
        Id = drink.Id,
        DrinkName = drink.DrinkName,
        DrinkPrice = drink.DrinkPrice,
        DrinkType = drink.DrinkType,
        IsActive = drink.IsActive,
        DrinkImageLink = drink.DrinkImageLink
    };

    // Helper: converts a Customer domain entity into a CustomerTransferDto for JSON serialization.
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

    // Helper: converts a LoyaltyCard domain entity into a CardTransferDto for JSON serialization.
    private static CardTransferDto MapCardToDto(LoyaltyCard card) => new()
    {
        Id = card.Id,
        CustomerId = card.CustomerId,
        IsActive = card.IsActive
    };

    // Helper: converts a Purchase domain entity into a PurchaseTransferDto for JSON serialization.
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

    // Helper: converts a LogEntry domain entity into a LogTransferDto for JSON serialization.
    private static LogTransferDto MapLogToDto(LogEntry log) => new()
    {
        Id = log.Id,
        ObjectId = log.ObjectId,
        ObjectType = log.ObjectType,
        ActionType = log.ActionType,
        Description = log.Description,
        TimeStamp = log.TimeStamp
    };
}