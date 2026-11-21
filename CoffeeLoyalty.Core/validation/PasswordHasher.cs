using System.Security.Cryptography;

namespace CoffeeLoyalty.Core.Validation;

// Hashes passwords with a random salt using PBKDF2 and verifies them in constant time.
public static class PasswordHasher
{
    // Salt size in bytes (128-bit random salt per password).
    private const int SaltSize = 16;

    // Derived subkey size in bytes (256-bit key).
    private const int KeySize = 32;

    // PBKDF2 iteration count for key stretching.
    private const int Iterations = 100_000;

    // Delimiter separating iterations, salt, and hash segments in the stored string.
    private const char Delimiter = '.';

    // Hashes a non-empty password with a fresh random salt and returns a formatted string.
    public static string HashPassword(string password)
    {
        // Reject null, empty, or whitespace-only passwords early.
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        // Generate a unique random salt for every password.
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);

        // Derive a key using PBKDF2-SHA256.
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize);

        string saltBase64 = Convert.ToBase64String(salt);
        string hashBase64 = Convert.ToBase64String(hash);

        // Store iterations, salt, and hash together so verification can parse them later.
        return $"{Iterations}{Delimiter}{saltBase64}{Delimiter}{hashBase64}";
    }

    // Verifies a password against a stored salted hash without throwing on malformed input.
    public static bool VerifyPassword(string password, string? storedHash)
    {
        // Return false safely on missing or malformed inputs instead of throwing.
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        string[] parts = storedHash.Split(Delimiter);
        if (parts.Length != 3)
        {
            return false;
        }

        // Parse iteration count and validate it is positive.
        if (!int.TryParse(parts[0], out int iterations) || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;

        try
        {
            // Decode salt and expected hash from Base64.
            salt = Convert.FromBase64String(parts[1]);
            expectedHash = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expectedHash.Length == 0)
        {
            return false;
        }

        // Recompute PBKDF2 using the stored salt and iteration count.
        byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);

        // Compare hashes in constant time to reduce timing side-channel risk.
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}