using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Interfaces.Services;
using CoffeeLoyalty.Core.interfaces.services;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.resources;
using CoffeeLoyalty.Core.Validation;
using System.Security.Cryptography;
using System.Text;

namespace CoffeeLoyalty.Core.Services;

/// <summary>
/// Handles employee authentication and uses the shared EmployeeSession for current-user state.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly IEmployeeRepository _employees;
    private readonly ILogService _logs;
    private readonly EmployeeSession _session;

    /// <summary>Creates an authentication service sharing the application's employee session.</summary>
    public AuthService(IEmployeeRepository employees, ILogService logs, EmployeeSession session)
    {
        _employees = employees ?? throw new ArgumentNullException(nameof(employees));
        _logs = logs ?? throw new ArgumentNullException(nameof(logs));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <summary>Returns the currently signed-in employee, if any.</summary>
    public Employee? CurrentEmployee => _session.CurrentEmployee;

    /// <summary>Returns the active role, if signed in.</summary>
    public UserRole? CurrentRole => _session.CurrentRole;

    /// <summary>Indicates whether there is an active employee session.</summary>
    public bool IsAuthenticated => _session.IsAuthenticated;

    /// <summary>Authenticates an active employee using role-appropriate credential checks.</summary>
    public async Task<OperationResult> LoginAsync(
        string employeeName,
        UserRole selectedRole,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Record the previous actor before ending a session during a user switch.
        if (_session.IsAuthenticated)
        {
            await LogoutAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(employeeName) || !Enum.IsDefined(selectedRole))
        {
            await LogFailureAsync("ErrorLogin", CoreResources.LogAuthInvalidAttempt, cancellationToken);
            return InvalidCredentials();
        }

        try
        {
            Employee? employee = await _employees.GetByNameAsync(employeeName.Trim(), cancellationToken);
            if (employee is null || employee.Id <= 0 || !employee.IsActive ||
                employee.EmployeeRole != selectedRole)
            {
                await LogFailureAsync("ErrorLogin", CoreResources.LogAuthDenied, cancellationToken);
                return InvalidCredentials();
            }

            if (selectedRole == UserRole.Manager)
            {
                if (string.IsNullOrEmpty(password))
                {
                    await LogFailureAsync("ErrorLogin", CoreResources.LogAuthDenied, cancellationToken);
                    return InvalidCredentials();
                }

                bool validModern = PasswordHasher.VerifyPassword(password, employee.EmployeePassword);
                bool validLegacy = !validModern && VerifyLegacySeedPassword(
                    employee.EmployeeName, password, employee.EmployeePassword);

                if (!validModern && !validLegacy)
                {
                    await LogFailureAsync("ErrorLogin", CoreResources.LogAuthDenied, cancellationToken);
                    return InvalidCredentials();
                }

                if (validLegacy)
                {
                    // Upgrade the legacy seeded hash only after successful verification.
                    employee.EmployeePassword = PasswordHasher.HashPassword(password);
                    if (!await _employees.UpdateAsync(employee, cancellationToken))
                    {
                        await LogFailureAsync("ErrorLogin", CoreResources.LogAuthFailed, cancellationToken);
                        return OperationResult.Failure(CoreResources.ErrorAuthUpdateFailed, "AUTH_UPDATE_FAILED");
                    }
                }
            }

            // Cashiers are authenticated by active employee account and selected role.
            cancellationToken.ThrowIfCancellationRequested();
            _session.SetCurrentEmployee(employee);
            await LogInfoBestEffortAsync("LogLogin", CoreResources.LogAuthSignedIn, employee.Id, cancellationToken);
            return OperationResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A cancelled login must not leave an active session behind.
            _session.Clear();
            throw;
        }
        catch (Exception)
        {
            _session.Clear();
            await LogFailureAsync("ErrorLogin", CoreResources.LogAuthFailed, cancellationToken);
            return OperationResult.Failure(CoreResources.ErrorAuthFailed, "AUTH_FAILED");
        }
    }

    /// <summary>Records a sign-out event, then removes the employee's permissions.</summary>
    public async Task<OperationResult> LogoutAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Employee? actor = _session.CurrentEmployee;
        if (actor is null)
        {
            return OperationResult.Success();
        }

        try
        {
            // Log before clearing: LogService reads the actor from this same session.
            await LogInfoBestEffortAsync("LogLogout", CoreResources.LogAuthSignedOut, actor.Id, cancellationToken);
            return OperationResult.Success();
        }
        finally
        {
            _session.Clear();
        }
    }

    /// <summary>Managers may use cashier features, but cashiers cannot use manager features.</summary>
    public bool HasPermission(UserRole requiredRole)
    {
        if (!Enum.IsDefined(requiredRole) || !_session.IsAuthenticated ||
            _session.CurrentEmployee?.IsActive != true)
        {
            return false;
        }

        return _session.CurrentRole == UserRole.Manager ||
            (_session.CurrentRole == UserRole.Cashier && requiredRole == UserRole.Cashier);
    }

    private static OperationResult InvalidCredentials() =>
        OperationResult.Failure(CoreResources.ErrorAuthInvalidCredentials, "INVALID_CREDENTIALS");

    // Only old demo seed accounts used SHA256(username:password). Do not support it for arbitrary accounts.
    private static bool VerifyLegacySeedPassword(string employeeName, string password, string? storedHash)
    {
        if (employeeName != "manager" || storedHash is null || storedHash.Length != 64)
        {
            return false;
        }

        byte[] expected;
        try
        {
            expected = Convert.FromHexString(storedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        // Accept only the original demonstration account hash, not arbitrary legacy hashes.
        byte[] originalSeedHash = Convert.FromHexString("9807AD379BAEF77702641F0AC059BCF14A9AE3471F2B5FA15F9FFC1DDDB1CE4F");
        if (!CryptographicOperations.FixedTimeEquals(expected, originalSeedHash))
        {
            return false;
        }

        byte[] actual = SHA256.HashData(Encoding.UTF8.GetBytes($"{employeeName}:{password}"));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private async Task LogFailureAsync(string action, string description, CancellationToken token)
    {
        try
        {
            await _logs.LogErrorAsync("EMPLOYEES", action, description, cancellationToken: token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Audit failure must not disclose implementation details or credentials.
        }
    }

    private async Task LogInfoBestEffortAsync(string action, string description, int employeeId, CancellationToken token)
    {
        try
        {
            await _logs.LogInfoAsync("EMPLOYEES", action, description, employeeId, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // A failed audit write must not strand an authenticated employee at logout.
        }
    }
}
