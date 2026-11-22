
namespace CoffeeLoyalty.Core.Enums;

// Defines the available user roles in CoffeeLoyalty.
// Using an enum avoids hardcoded role names throughout the application.
public enum UserRole
{
    // Cashiers handle sales, checkout, and customer-related operations.
    // They do not have access to manager-only administration features.
    Cashier = 0,

    // Managers have full access to administrative features,
    // including menu management, logs, import/export, and settings.
    Manager = 1
}
