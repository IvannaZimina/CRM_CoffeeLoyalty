using CoffeeLoyalty.Core.Enums;

namespace CoffeeLoyalty.Core.Interfaces.Services;

// Exposes only the information needed for audit logging and role checks.
// Authentication owns the session; readers cannot change it through this interface.
public interface ICurrentEmployeeContext
{
    int? EmployeeId { get; }

    UserRole? CurrentRole { get; }

    bool IsAuthenticated { get; }
}
