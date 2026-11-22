
using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.Validation;

namespace CoffeeLoyalty.Core.Models;

// Represents a coffee shop employee with a role and account status.
// Inherits the employee ID from BaseEntity and supports account deactivation.
public class Employee : BaseEntity, IActivatable
{
    private string _employeeName = string.Empty;
    private UserRole _employeeRole;
    private string? _employeePassword;

    // Creates an employee with a name and an assigned role.
    // The password hash can be provided later, for example during account setup.
    // New employee accounts are active by default.
    public Employee(
        string employeeName,
        UserRole employeeRole,
        string? employeePassword = null,
        bool isActive = true)
    {
        EmployeeName = employeeName;
        EmployeeRole = employeeRole;
        EmployeePassword = employeePassword;
        IsActive = isActive;
    }

    // Stores the employee's name or login.
    // Removes extra spaces and rejects empty or whitespace-only names.
    public string EmployeeName
    {
        get => _employeeName;
        set
        {
            if (!ValidationHelper.HasText(value))
            {
                throw new ArgumentException(
                    "Employee name cannot be empty.",
                    nameof(value));
            }

            _employeeName = value.Trim();
        }
    }

    // Defines which operations the employee is allowed to perform.
    // Only roles declared in UserRole are accepted.
    public UserRole EmployeeRole
    {
        get => _employeeRole;
        set
        {
            // C# allows casting arbitrary integers to enums.
            // This check prevents values such as (UserRole)99.
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Invalid employee role.");
            }

            _employeeRole = value;
        }
    }

    // Stores a password hash, never the plain-text password.
    // Null is allowed while preparing an account, but an empty
    // or whitespace-only hash is not considered valid.
    // Password hashing and verification belong to the authentication layer.
    public string? EmployeePassword
    {
        get => _employeePassword;
        set
        {
            if (value is not null && !ValidationHelper.HasText(value))
            {
                throw new ArgumentException(
                    "Password hash cannot be empty.",
                    nameof(value));
            }

            // Keep the hash unchanged so password verification
            // receives exactly the value that was originally stored.
            _employeePassword = value;
        }
    }

    // Allows an employee account to be disabled without deleting it.
    // Inactive employees should be prevented from logging in by AuthService.
    public bool IsActive { get; set; }
}
