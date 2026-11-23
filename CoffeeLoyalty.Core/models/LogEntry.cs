using CoffeeLoyalty.Core.resources;

using CoffeeLoyalty.Core.Validation;

namespace CoffeeLoyalty.Core.Models;

// Represents a single audit or error record in the application.
// Log entries describe what happened, which object was involved,
// and when the event occurred.
public class LogEntry : BaseEntity
{
    private int? _objectId;
    private string _objectType = string.Empty;
    private string _actionType = string.Empty;
    private string _description = string.Empty;
    private DateTime _timeStamp;

    // Creates a log entry with the required event information.
    // ObjectId is optional because some events are system-wide.
    // If no timestamp is provided, the current UTC time is used.
    public LogEntry(
        string objectType,
        string actionType,
        string description,
        int? objectId = null,
        DateTime? timeStamp = null,
        int? employeeId = null)
    {
        ObjectId = objectId;
        EmployeeId = employeeId;
        ObjectType = objectType;
        ActionType = actionType;
        Description = description;
        TimeStamp = timeStamp ?? DateTime.UtcNow;
    }

    // Identifies the database record related to this event.
    // Null is allowed for general system events, such as startup errors.
    // When an ID is provided, it must be a positive integer.
    public int? ObjectId
    {
        get => _objectId;
        set
        {
            if (value.HasValue &&
                !ValidationHelper.IsPositive(value.Value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    CoreResources.ValLogObjectIdPositive);
            }

            _objectId = value;
        }
    }

    // Identifies the employee who performed the action; null is allowed for system and legacy events.
    public int? EmployeeId { get; set; }

    // Describes the type of object involved in the event.
    // Examples: CUSTOMERS, PURCHASES, EMPLOYEES, or System.
    // System-wide events still need an object type for SQLite.
    public string ObjectType
    {
        get => _objectType;
        set
        {
            if (!ValidationHelper.HasText(value))
            {
                throw new ArgumentException(
                    CoreResources.ValLogObjectTypeRequired,
                    nameof(value));
            }

            _objectType = value.Trim();
        }
    }

    // Identifies the action or error being recorded.
    // Examples: LogPurchase, LogCustomerUpdate, or ErrorLogin.
    // Specific event names are managed by the logging service.
    public string ActionType
    {
        get => _actionType;
        set
        {
            if (!ValidationHelper.HasText(value))
            {
                throw new ArgumentException(
                    CoreResources.ValLogActionTypeRequired,
                    nameof(value));
            }

            _actionType = value.Trim();
        }
    }

    // Stores a readable explanation of the event.
    // Empty descriptions are rejected so log records remain useful.
    public string Description
    {
        get => _description;
        set
        {
            if (!ValidationHelper.HasText(value))
            {
                throw new ArgumentException(
                    CoreResources.ValLogDescriptionRequired,
                    nameof(value));
            }

            _description = value.Trim();
        }
    }

    // Stores the date and time when the event occurred.
    // New records use UTC by default for consistent logging.
    // A default DateTime value is rejected as an invalid timestamp.
    public DateTime TimeStamp
    {
        get => _timeStamp;
        set
        {
            if (value == default)
            {
                throw new ArgumentException(
                    CoreResources.ValLogTimestampInvalid,
                    nameof(value));
            }

            _timeStamp = value;
        }
    }
}
