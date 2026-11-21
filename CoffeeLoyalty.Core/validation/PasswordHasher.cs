using System.Globalization;
using System.Security.Cryptography;

namespace CoffeeLoyalty.Core.Validation;

// Hashes passwords with a random salt and verifies them using PBKDF2-SHA256.
public static class PasswordHasher
{
    // Use a 16-byte random salt so identical passwords produce different hashes.
    private const int SaltSize = 16;

    // Use a 32-byte derived hash key for strong security.
    private const int KeySize = 32;

    // Default iteration count for newly generated password hashes.
    private const int Iterations = 600_000;

    // Accept reasonable iteration counts while blocking weak or overly large values.
    private const int MinIterations = 100_000;
    private const int MaxIterations = 2_000_000;

    // Reject overly long strings early to avoid wasting work on bad input.
    private const int MaxStoredHashLength = 128;

    // Separate iterations, salt, and hash with a dot in the stored string.
    private const char Delimiter = '.';

    // Creates a salted PBKDF2 hash string for a given password.
    public static string HashPassword(string password)
    {
        // Reject null, empty, or whitespace-only passwords right away.
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        // Create a fresh random salt for this password.
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);

        // Derive the password hash with PBKDF2 and SHA-256.
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize);

        // Combine iteration count, Base64 salt, and Base64 hash into one string.
        return $"{Iterations}{Delimiter}" +
               $"{Convert.ToBase64String(salt)}{Delimiter}" +
               $"{Convert.ToBase64String(hash)}";
    }

    // Verifies a password against a stored hash string without throwing on bad input.
    public static bool VerifyPassword(string password, string? storedHash)
    {
        // Return false safely if the password is blank or the stored hash is missing or too long.
        if (string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(storedHash) ||
            storedHash.Length > MaxStoredHashLength)
        {
            return false;
        }

        // Split the stored value into iterations, salt, and hash parts.
        string[] parts = storedHash.Split(Delimiter);

        // A valid stored hash must have exactly three parts.
        if (parts.Length != 3)
        {
            return false;
        }

        // Parse the iteration count and make sure it stays within allowed bounds.
        if (!int.TryParse(
                parts[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int iterations) ||
            iterations < MinIterations ||
            iterations > MaxIterations)
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;

        try
        {
            // Decode salt and hash bytes from Base64.
            salt = Convert.FromBase64String(parts[1]);
            expectedHash = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            // Return false if the Base64 text is invalid or corrupted.
            return false;
        }

        // Make sure decoded salt and hash match the expected byte lengths.
        if (salt.Length != SaltSize || expectedHash.Length != KeySize)
        {
            return false;
        }

        // Recompute the hash from the input password and stored salt/iterations.
        byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            KeySize);

        // Compare hashes in constant time to prevent timing attacks.
        return CryptographicOperations.FixedTimeEquals(
            actualHash,
            expectedHash);
    }
}