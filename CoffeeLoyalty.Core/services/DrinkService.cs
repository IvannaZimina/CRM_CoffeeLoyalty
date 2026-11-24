using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.interfaces.services;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Interfaces.Services;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.resources;
using CoffeeLoyalty.Core.Validation;

namespace CoffeeLoyalty.Core.Services;

// Coordinates drink menu queries, role checks, data validation, and audit logging.
// Keeps WPF and database details out of the service layer.
public class DrinkService : IDrinkService
{
    private const string AuditObjectType = "Drink";

    private readonly IDrinkRepository _drinkRepository;
    private readonly IAuthService _authService;
    private readonly ILogService _logService;

    // Injects repository, authentication, and logging dependencies.
    // Throws ArgumentNullException if any required dependency is missing.
    public DrinkService(
        IDrinkRepository drinkRepository,
        IAuthService authService,
        ILogService logService)
    {
        _drinkRepository = drinkRepository ?? throw new ArgumentNullException(nameof(drinkRepository));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    // Returns all drinks, including inactive/archived ones.
    // Restricted to Manager users with an active session.
    public async Task<OperationResult<IReadOnlyList<Drink>>> GetAllDrinksAsync(
        CancellationToken cancellationToken = default)
    {
        // Verify active session and Manager permission before exposing inactive items.
        OperationResult permissionCheck = await EnsureAuthenticatedAndRoleAsync(
            UserRole.Manager,
            "GetAllDrinks",
            LogActionType.GetAllDrinksUnauthorized,
            LogActionType.GetAllDrinksForbidden,
            cancellationToken);

        if (permissionCheck.IsFailure)
        {
            return OperationResult<IReadOnlyList<Drink>>.Failure(
                permissionCheck.ErrorMessage ?? CoreResources.DrinkServiceAccessDenied,
                permissionCheck.ErrorCode);
        }

        return await _drinkRepository.GetAllAsync(cancellationToken);
    }

    // Returns only active drinks available for selection and checkout.
    // Accessible to authenticated Cashiers and Managers.
    public async Task<OperationResult<IReadOnlyList<Drink>>> GetAvailableDrinksAsync(
        CancellationToken cancellationToken = default)
    {
        // Ensure an employee session is active before reading menu items.
        OperationResult authCheck = await EnsureAuthenticatedAsync("GetAvailableDrinks", LogActionType.GetAvailableDrinksUnauthorized, cancellationToken);
        if (authCheck.IsFailure)
        {
            return OperationResult<IReadOnlyList<Drink>>.Failure(
                authCheck.ErrorMessage ?? CoreResources.DrinkServiceAuthRequired,
                authCheck.ErrorCode);
        }

        return await _drinkRepository.GetAvailableAsync(cancellationToken);
    }

    // Returns a single drink by its ID for any authenticated user.
    // Rejects non-positive IDs without hitting the database.
    public async Task<OperationResult<Drink>> GetDrinkByIdAsync(
        int drinkId,
        CancellationToken cancellationToken = default)
    {
        OperationResult authCheck = await EnsureAuthenticatedAsync("GetDrinkById", LogActionType.GetDrinkByIdUnauthorized, cancellationToken);
        if (authCheck.IsFailure)
        {
            return OperationResult<Drink>.Failure(
                authCheck.ErrorMessage ?? CoreResources.DrinkServiceAuthRequired,
                authCheck.ErrorCode);
        }

        if (!ValidationHelper.IsPositive(drinkId))
        {
            return OperationResult<Drink>.Failure(
                CoreResources.DrinkServiceIdPositive,
                "INVALID_ID");
        }

        return await _drinkRepository.GetByIdAsync(drinkId, cancellationToken);
    }

    // Returns active drinks matching the requested drink type (case-insensitive).
    // Validates that drinkType is not empty or whitespace.
    public async Task<OperationResult<IReadOnlyList<Drink>>> GetDrinksByTypeAsync(
        string drinkType,
        CancellationToken cancellationToken = default)
    {
        OperationResult authCheck = await EnsureAuthenticatedAsync("GetDrinksByType", LogActionType.GetDrinksByTypeUnauthorized, cancellationToken);
        if (authCheck.IsFailure)
        {
            return OperationResult<IReadOnlyList<Drink>>.Failure(
                authCheck.ErrorMessage ?? CoreResources.DrinkServiceAuthRequired,
                authCheck.ErrorCode);
        }

        if (!ValidationHelper.HasText(drinkType))
        {
            return OperationResult<IReadOnlyList<Drink>>.Failure(
                CoreResources.DrinkServiceTypeRequired,
                "INVALID_INPUT");
        }

        OperationResult<IReadOnlyList<Drink>> allDrinksResult = await _drinkRepository.GetAllAsync(cancellationToken);
        if (allDrinksResult.IsFailure || allDrinksResult.Value is null)
        {
            return allDrinksResult;
        }

        string normalizedType = drinkType.Trim();
        IReadOnlyList<Drink> filtered = allDrinksResult.Value
            .Where(d => d.IsActive && string.Equals(d.DrinkType, normalizedType, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return OperationResult<IReadOnlyList<Drink>>.Success(filtered);
    }

    // Searches drinks by name or type matching searchText.
    // Managers search across all drinks; Cashiers search only active drinks.
    public async Task<OperationResult<IReadOnlyList<Drink>>> SearchDrinksAsync(
        string searchText,
        CancellationToken cancellationToken = default)
    {
        OperationResult authCheck = await EnsureAuthenticatedAsync("SearchDrinks", LogActionType.SearchDrinksUnauthorized, cancellationToken);
        if (authCheck.IsFailure)
        {
            return OperationResult<IReadOnlyList<Drink>>.Failure(
                authCheck.ErrorMessage ?? CoreResources.DrinkServiceAuthRequired,
                authCheck.ErrorCode);
        }

        // Managers can see inactive drinks in search results; Cashiers only see active ones.
        OperationResult<IReadOnlyList<Drink>> sourceResult = _authService.HasPermission(UserRole.Manager)
            ? await _drinkRepository.GetAllAsync(cancellationToken)
            : await _drinkRepository.GetAvailableAsync(cancellationToken);

        if (sourceResult.IsFailure || sourceResult.Value is null)
        {
            return sourceResult;
        }

        // Return the full accessible list when no search filter is provided.
        if (!ValidationHelper.HasText(searchText))
        {
            return OperationResult<IReadOnlyList<Drink>>.Success(sourceResult.Value);
        }

        string query = searchText.Trim();
        IReadOnlyList<Drink> matches = sourceResult.Value
            .Where(d =>
                d.DrinkName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                d.DrinkType.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return OperationResult<IReadOnlyList<Drink>>.Success(matches);
    }

    // Adds a new drink to the menu after Manager role and data validation checks.
    // LogService records the active employee ID from the shared session automatically.
    public async Task<OperationResult<Drink>> AddDrinkAsync(
        Drink drink,
        CancellationToken cancellationToken = default)
    {
        OperationResult permissionCheck = await EnsureAuthenticatedAndRoleAsync(
            UserRole.Manager,
            "AddDrink",
            LogActionType.AddDrinkUnauthorized,
            LogActionType.AddDrinkForbidden,
            cancellationToken);

        if (permissionCheck.IsFailure)
        {
            return OperationResult<Drink>.Failure(
                permissionCheck.ErrorMessage ?? CoreResources.DrinkServiceAccessDenied,
                permissionCheck.ErrorCode);
        }

        OperationResult validationResult = DrinkValidator.Validate(drink);
        if (validationResult.IsFailure)
        {
            await TryLogErrorAsync(
                LogActionType.AddDrinkValidationFailed,
                validationResult.ErrorMessage ?? CoreResources.DrinkServiceInvalidDrinkData,
                null,
                cancellationToken);

            return OperationResult<Drink>.Failure(
                validationResult.ErrorMessage ?? CoreResources.DrinkServiceInvalidDrinkData,
                validationResult.ErrorCode);
        }

        OperationResult<Drink> addResult = await _drinkRepository.AddAsync(drink, cancellationToken);
        if (addResult.IsFailure || addResult.Value is null)
        {
            await TryLogErrorAsync(
                LogActionType.AddDrinkFailed,
                string.Format(CoreResources.DrinkLogAddFailed, drink.DrinkName, addResult.ErrorMessage),
                drink.Id > 0 ? drink.Id : null,
                cancellationToken);

            return addResult;
        }

        await TryLogInfoAsync(
            LogActionType.DrinkCreated,
            string.Format(
                CoreResources.DrinkLogCreated,
                addResult.Value.DrinkName,
                addResult.Value.DrinkType,
                addResult.Value.DrinkPrice),
            addResult.Value.Id,
            cancellationToken);

        return addResult;
    }

    // Updates an existing drink's details for Manager users.
    // Validates the drink ID and fields, then records an audit or error log entry.
    public async Task<OperationResult> UpdateDrinkAsync(
        Drink drink,
        CancellationToken cancellationToken = default)
    {
        OperationResult permissionCheck = await EnsureAuthenticatedAndRoleAsync(
            UserRole.Manager,
            "UpdateDrink",
            LogActionType.UpdateDrinkUnauthorized,
            LogActionType.UpdateDrinkForbidden,
            cancellationToken);

        if (permissionCheck.IsFailure)
        {
            return permissionCheck;
        }

        OperationResult validationResult = DrinkValidator.Validate(drink, requirePositiveId: true);
        if (validationResult.IsFailure)
        {
            await TryLogErrorAsync(
                LogActionType.UpdateDrinkValidationFailed,
                validationResult.ErrorMessage ?? CoreResources.DrinkServiceInvalidDrinkData,
                drink is not null && drink.Id > 0 ? drink.Id : null,
                cancellationToken);

            return validationResult;
        }

        OperationResult updateResult = await _drinkRepository.UpdateAsync(drink!, cancellationToken);
        if (updateResult.IsFailure)
        {
            await TryLogErrorAsync(
                LogActionType.UpdateDrinkFailed,
                string.Format(
                    CoreResources.DrinkLogUpdateFailed,
                    drink!.DrinkName,
                    drink.Id,
                    updateResult.ErrorMessage),
                drink.Id,
                cancellationToken);

            return updateResult;
        }

        await TryLogInfoAsync(
            LogActionType.DrinkUpdated,
            string.Format(
                CoreResources.DrinkLogUpdated,
                drink!.DrinkName,
                drink.Id,
                drink.DrinkType,
                drink.DrinkPrice),
            drink.Id,
            cancellationToken);

        return OperationResult.Success();
    }

    // Activates or softly deactivates a drink in the menu for Manager users.
    // Soft deactivation keeps historical purchases intact.
    public async Task<OperationResult> SetActiveStatusAsync(
        int drinkId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        OperationResult permissionCheck = await EnsureAuthenticatedAndRoleAsync(
            UserRole.Manager,
            "SetDrinkActiveStatus",
            LogActionType.SetDrinkActiveStatusUnauthorized,
            LogActionType.SetDrinkActiveStatusForbidden,
            cancellationToken);

        if (permissionCheck.IsFailure)
        {
            return permissionCheck;
        }

        if (!ValidationHelper.IsPositive(drinkId))
        {
            await TryLogErrorAsync(
                LogActionType.SetDrinkActiveStatusFailed,
                CoreResources.DrinkLogUpdateInvalidId,
                null,
                cancellationToken);

            return OperationResult.Failure(
                CoreResources.DrinkServiceIdPositive,
                "INVALID_ID");
        }

        OperationResult statusResult = await _drinkRepository.SetActiveStatusAsync(
            drinkId,
            isActive,
            cancellationToken);

        if (statusResult.IsFailure)
        {
            await TryLogErrorAsync(
                LogActionType.SetDrinkActiveStatusFailed,
                string.Format(CoreResources.DrinkLogSetActiveFailed, isActive, drinkId, statusResult.ErrorMessage),
                drinkId,
                cancellationToken);

            return statusResult;
        }

        LogActionType actionType = isActive ? LogActionType.DrinkActivated : LogActionType.DrinkDeactivated;
        string description = isActive
            ? string.Format(CoreResources.DrinkLogActivated, drinkId)
            : string.Format(CoreResources.DrinkLogDeactivated, drinkId);

        await TryLogInfoAsync(actionType, description, drinkId, cancellationToken);

        return OperationResult.Success();
    }

    // Checks that an employee session is active; logs an error if unauthenticated.
    private async Task<OperationResult> EnsureAuthenticatedAsync(
        string actionName,
        LogActionType unauthorizedAction,
        CancellationToken cancellationToken)
    {
        if (!_authService.IsAuthenticated || _authService.CurrentEmployee is null)
        {
            await TryLogErrorAsync(
                unauthorizedAction,
                string.Format(CoreResources.DrinkLogUnauthorized, actionName),
                null,
                cancellationToken);

            return OperationResult.Failure(
                CoreResources.DrinkServiceAuthRequired,
                "UNAUTHENTICATED");
        }

        return OperationResult.Success();
    }

    // Checks both authentication and whether the current user has the required role.
    private async Task<OperationResult> EnsureAuthenticatedAndRoleAsync(
        UserRole requiredRole,
        string actionName,
        LogActionType unauthorizedAction,
        LogActionType forbiddenAction,
        CancellationToken cancellationToken)
    {
        OperationResult authResult = await EnsureAuthenticatedAsync(actionName, unauthorizedAction, cancellationToken);
        if (authResult.IsFailure)
        {
            return authResult;
        }

        if (!_authService.HasPermission(requiredRole))
        {
            await TryLogErrorAsync(
                forbiddenAction,
                string.Format(CoreResources.DrinkLogForbidden, _authService.CurrentRole, actionName, requiredRole),
                null,
                cancellationToken);

            return OperationResult.Failure(
                string.Format(CoreResources.DrinkServiceRoleRequired, requiredRole),
                "FORBIDDEN");
        }

        return OperationResult.Success();
    }

    // Writes an informational audit log entry; LogService records employee ID from session.
    private Task TryLogInfoAsync(
        LogActionType actionType,
        string description,
        int? objectId,
        CancellationToken cancellationToken) =>
        _logService.LogInfoAsync(
            AuditObjectType,
            actionType,
            description,
            objectId,
            cancellationToken);

    // Writes an error audit log entry; LogService records employee ID from session.
    private Task TryLogErrorAsync(
        LogActionType actionType,
        string description,
        int? objectId,
        CancellationToken cancellationToken) =>
        _logService.LogErrorAsync(
            AuditObjectType,
            actionType,
            description,
            objectId,
            cancellationToken);
}
