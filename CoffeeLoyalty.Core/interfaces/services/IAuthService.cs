
using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.Models;

namespace CoffeeLoyalty.Core.Interfaces.Services;

// Defines employee authentication, session management, and role checks.
public interface IAuthService
{
    // Currently signed-in employee, or null when logged out.
    Employee? CurrentEmployee { get; }

    // Role of the active employee session.
    UserRole? CurrentRole { get; }

    // Indicates whether an employee is currently signed in.
    bool IsAuthenticated { get; }

    // Authenticates an employee against the selected role.
    // Cashiers can sign in without a password; managers require one.
    Task<OperationResult> LoginAsync(
        string employeeName,
        UserRole selectedRole,
        string? password = null,
        CancellationToken cancellationToken = default);

    // Ends the current session and allows the logout event to be logged.
    Task<OperationResult> LogoutAsync(
        CancellationToken cancellationToken = default);

    // Checks whether the current role permits the requested access level.
    bool HasPermission(UserRole requiredRole);
}
