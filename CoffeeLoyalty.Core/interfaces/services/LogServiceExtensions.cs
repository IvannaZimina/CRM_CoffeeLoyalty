using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.resources;

namespace CoffeeLoyalty.Core.interfaces.services;

// Typed audit operations over the existing string-based logging contract.
public static class LogServiceExtensions
{
    public static Task<OperationResult> LogInfoAsync(
        this ILogService service, string objectType, LogActionType actionType,
        string description, int? objectId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        cancellationToken.ThrowIfCancellationRequested();
        return Enum.IsDefined(actionType)
            ? service.LogInfoAsync(objectType, actionType.ToString(), description, objectId, cancellationToken)
            : Task.FromResult(OperationResult.Failure(CoreResources.LogServiceInvalidEntry, "INVALID_LOG"));
    }

    public static Task<OperationResult> LogErrorAsync(
        this ILogService service, string objectType, LogActionType actionType,
        string description, int? objectId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        cancellationToken.ThrowIfCancellationRequested();
        return Enum.IsDefined(actionType)
            ? service.LogErrorAsync(objectType, actionType.ToString(), description, objectId, cancellationToken)
            : Task.FromResult(OperationResult.Failure(CoreResources.LogServiceInvalidEntry, "INVALID_LOG"));
    }

    // Delegate invalid filters as empty strings so the existing service checks authorization first.
    public static Task<OperationResult<IReadOnlyList<LogEntry>>> GetByActionTypeAsync(
        this ILogService service, LogActionType actionType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        cancellationToken.ThrowIfCancellationRequested();
        return service.GetByActionTypeAsync(
            Enum.IsDefined(actionType) ? actionType.ToString() : string.Empty, cancellationToken);
    }
}
