using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.Interfaces.Services;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.resources;

namespace CoffeeLoyalty.Core.Services;

// Maintains one active employee session for authentication and logging.
public sealed class EmployeeSession : ICurrentEmployeeContext
{
    private Employee? _currentEmployee;

    // The employee is exposed to AuthService; LogService receives only the read-only interface.
    public Employee? CurrentEmployee => _currentEmployee;

    public int? EmployeeId => _currentEmployee?.Id;

    public UserRole? CurrentRole => _currentEmployee?.EmployeeRole;

    public bool IsAuthenticated => _currentEmployee is not null;

    // Called by AuthService only after successful authentication.
    public void SetCurrentEmployee(Employee employee)
    {
        ArgumentNullException.ThrowIfNull(employee);

        if (employee.Id <= 0 || !employee.IsActive)
        {
            throw new ArgumentException(
                CoreResources.LogSessionInvalidEmployee,
                nameof(employee));
        }

        _currentEmployee = employee;
    }

    // Clears all session state, including the previous manager's permissions.
    public void Clear()
    {
        _currentEmployee = null;
    }
}
