using System.IO;

namespace CoffeeLoyalty.Core.Validation;

// Contains simple checks and normalization helpers used across the application.
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

    // Normalizes a relative image path and returns null for empty, rooted, or URI values.
    public static string? NormalizeRelativeImagePath(string? imagePath)
    {
        if (!HasText(imagePath))
        {
            return null;
        }

        string trimmed = imagePath!.Trim();

        // Reject rooted/drive paths or scheme URIs so only relative paths remain.
        if (Path.IsPathRooted(trimmed) ||
            trimmed.Contains("://", StringComparison.Ordinal) ||
            trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed.Replace('\\', '/');
    }
}