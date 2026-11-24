using System.Text.RegularExpressions;
using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.interfaces.services;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Interfaces.Services;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.resources;

namespace CoffeeLoyalty.Core.Services;

// Coordinates audit recording and manager-only log retrieval without depending on AuthService.
public sealed class LogService : ILogService
{
    private readonly ILogRepository _repository;
    private readonly ICurrentEmployeeContext _currentEmployee;

    // One shared EmployeeSession instance is supplied as ICurrentEmployeeContext.
    public LogService(
        ILogRepository repository,
        ICurrentEmployeeContext currentEmployee)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _currentEmployee = currentEmployee ?? throw new ArgumentNullException(nameof(currentEmployee));
    }

    // Records an informational action. Legacy action names are kept unchanged.
    public Task<OperationResult> LogInfoAsync(
        string objectType,
        LogActionType actionType,
        string description,
        int? objectId = null,
        CancellationToken cancellationToken = default)
    {
        return WriteAsync(objectType, actionType, description, objectId, cancellationToken);
    }

    // Records an application error with sensitive details removed when recognizable.
    public Task<OperationResult> LogErrorAsync(
        string objectType,
        LogActionType actionType,
        string description,
        int? objectId = null,
        CancellationToken cancellationToken = default)
    {
        return WriteAsync(objectType, actionType, description, objectId, cancellationToken);
    }

    // Only an authenticated Manager can read the complete journal.
    public Task<OperationResult<IReadOnlyList<LogEntry>>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return ReadAsync(token => _repository.GetAllAsync(token), cancellationToken);
    }

    // Applies the same Manager-only permission check before date filtering.
    public Task<OperationResult<IReadOnlyList<LogEntry>>> GetByDateRangeAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        return ReadAsync(async token =>
        {
            if (startDate == default || endDate == default || startDate > endDate)
            {
                throw new ArgumentOutOfRangeException(nameof(endDate));
            }

            return await _repository.GetByDateRangeAsync(startDate, endDate, token);
        }, cancellationToken);
    }

    // Applies the same Manager-only permission check before action filtering.
    public Task<OperationResult<IReadOnlyList<LogEntry>>> GetByActionTypeAsync(
        LogActionType actionType,
        CancellationToken cancellationToken = default)
    {
        return ReadAsync(async token =>
        {
            if (!Enum.IsDefined(actionType))
            {
                throw new ArgumentException(nameof(actionType));
            }

            return await _repository.GetByActionTypeAsync(actionType.ToString(), token);
        }, cancellationToken);
    }

    // Records employee and object IDs separately and never exposes repository exception messages.
    private async Task<OperationResult> WriteAsync(
        string objectType,
        LogActionType actionType,
        string description,
        int? objectId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(objectType) ||
            !Enum.IsDefined(actionType) ||
            string.IsNullOrWhiteSpace(description) ||
            objectId is <= 0)
        {
            return OperationResult.Failure(CoreResources.LogServiceInvalidEntry, "INVALID_LOG");
        }

        try
        {
            LogEntry entry = new(
                objectType: objectType,
                actionType: actionType.ToString(),
                description: SafeDescription(description),
                objectId: objectId,
                employeeId: _currentEmployee.IsAuthenticated ? _currentEmployee.EmployeeId : null);

            int id = await _repository.AddAsync(entry, cancellationToken);

            return id > 0
                ? OperationResult.Success()
                : OperationResult.Failure(CoreResources.LogServiceWriteFailed, "LOG_WRITE_FAILED");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Do not surface credentials, SQL, stack traces, or other exception details.
            return OperationResult.Failure(CoreResources.LogServiceWriteFailed, "LOG_WRITE_FAILED");
        }
    }

    // Restricts all log queries in one place and returns safe failures.
    private async Task<OperationResult<IReadOnlyList<LogEntry>>> ReadAsync(
        Func<CancellationToken, Task<IReadOnlyList<LogEntry>>> query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_currentEmployee.IsAuthenticated ||
            _currentEmployee.CurrentRole != UserRole.Manager ||
            _currentEmployee.EmployeeId is not > 0)
        {
            // Never call LogErrorAsync from here: a log-viewing failure must not recursively log itself.
            return OperationResult<IReadOnlyList<LogEntry>>.Failure(
                CoreResources.LogServiceAccessDenied, "ACCESS_DENIED");
        }

        try
        {
            return OperationResult<IReadOnlyList<LogEntry>>.Success(
                await query(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ArgumentException)
        {
            return OperationResult<IReadOnlyList<LogEntry>>.Failure(
                CoreResources.LogServiceInvalidFilter, "INVALID_FILTER");
        }
        catch (Exception)
        {
            return OperationResult<IReadOnlyList<LogEntry>>.Failure(
                CoreResources.LogServiceReadFailed, "LOG_READ_FAILED");
        }
    }

    // Protects against common secret/stack-trace patterns; callers must still supply safe audit messages.
    private static string SafeDescription(string description)
    {
        string normalized = description.Trim();

        // Do not persist multiline exception messages, paths, or recognizable secret labels.
        if (normalized.Length > 512 ||
            normalized.Contains('\n') || normalized.Contains('\r') ||
            Regex.IsMatch(normalized,
                @"(?i)\b(password|passwd|pwd|secret|token|api[_-]?key|authorization|bearer|hash|exception|stack\s*trace)\b|(?:[A-Za-z]:\\|/(?:home|users)/)"))
        {
            return CoreResources.LogServiceDetailsRedacted;
        }

        return normalized;
    }
}
