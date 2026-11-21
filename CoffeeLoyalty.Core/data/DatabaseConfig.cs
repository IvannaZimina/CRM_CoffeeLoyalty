
using System.IO;

namespace CoffeeLoyalty.Core.Data;

/// <summary>
/// Provides the location of the SQLite database.
/// </summary>
public static class DatabaseConfig
{
    public static string DatabasePath =>
        Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "coffeeloyalty.db"
        );
}
