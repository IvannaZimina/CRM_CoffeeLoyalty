
using CoffeeLoyalty.Core.Models;

namespace CoffeeLoyalty.Core.Interfaces.Repositories;

// Defines asynchronous operations for storing and retrieving audit logs.
public interface ILogRepository
{
    // Saves a log entry and returns its generated database ID.
    Task<int> AddAsync(
        LogEntry logEntry,
        CancellationToken cancellationToken = default);

    // Retrieves all recorded events and errors.
    Task<IReadOnlyList<LogEntry>> GetAllAsync(
        CancellationToken cancellationToken = default);

    // Retrieves logs within the specified date and time range.
    Task<IReadOnlyList<LogEntry>> GetByDateRangeAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default);

    // Filters logs by action type, such as LogPurchase or ErrorLogin.
    Task<IReadOnlyList<LogEntry>> GetByActionTypeAsync(
        string actionType,
        CancellationToken cancellationToken = default);
}
