
using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Models;

namespace CoffeeLoyalty.Core.Interfaces.Services;

// Defines audit logging, error logging, and log retrieval operations.
public interface ILogService
{
    // Records an informational event or user action.
    Task<OperationResult> LogInfoAsync(
        string objectType,
        string actionType,
        string description,
        int? objectId = null,
        CancellationToken cancellationToken = default);

    // Records an application error without exposing sensitive details.
    Task<OperationResult> LogErrorAsync(
        string objectType,
        string actionType,
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
        string actionType,
        CancellationToken cancellationToken = default);
}
