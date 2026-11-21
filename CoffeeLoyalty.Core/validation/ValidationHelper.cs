
namespace CoffeeLoyalty.Core.Validation;

// Contains simple checks for values used in the application.
public static class ValidationHelper
{
    // Checks that text is not null, empty, or only spaces.
    public static bool HasText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    // Checks that a decimal number is zero or greater.
    public static bool IsNonNegative(decimal value)
    {
        return value >= 0m;
    }

    // Checks that an integer is zero or greater.
    public static bool IsNonNegative(int value)
    {
        return value >= 0;
    }

    // Checks that an integer is greater than zero.
    public static bool IsPositive(int value)
    {
        return value > 0;
    }

    // Checks that a decimal number is greater than zero.
    public static bool IsPositive(decimal value)
    {
        return value > 0m;
    }
}
