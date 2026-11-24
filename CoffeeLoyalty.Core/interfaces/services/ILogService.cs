using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Models;

namespace CoffeeLoyalty.Core.interfaces.services;

// Defines audit logging, error logging, and log retrieval operations.
public interface ILogService
{
    // Records an informational event or user action.
    Task<OperationResult> LogInfoAsync(
        string objectType,
        LogActionType actionType,
        string description,
        int? objectId = null,
        CancellationToken cancellationToken = default);

    // Records an application error without exposing sensitive details.
    Task<OperationResult> LogErrorAsync(
        string objectType,
        LogActionType actionType,
        string description,
        int? objectId = null,
        CancellationToken cancellationToken = default);

    // Retrieves all logs for authorized users.
    Task<OperationResult<IReadOnlyList<LogEntry>>> GetAllAsync(
        CancellationToken cancellationToken = default);

    // Retrieves logs within a specified date range.
    Task<OperationResult<IReadOnlyList<LogEntry>>> GetByDateRangeAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default);

    // Retrieves logs matching an event or error type.
    Task<OperationResult<IReadOnlyList<LogEntry>>> GetByActionTypeAsync(
        LogActionType actionType,
        CancellationToken cancellationToken = default);
}
