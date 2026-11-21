
using Microsoft.Data.Sqlite;

namespace CoffeeLoyalty.Core.Data;

/// <summary>
/// Provides initial reference and test data.
/// </summary>
public sealed class SeedDataFactory
{
    /// <summary>
    /// Runs initial data seeding after schema creation.
    /// </summary>
    public void Seed(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        // Seed data will be added with individual table tasks.
        // Use INSERT OR IGNORE or existence checks to avoid duplicates.

        SeedCategories(connection, transaction);
        SeedDrinks(connection, transaction);
        SeedEmployees(connection, transaction);
        SeedCustomers(connection, transaction);
        SeedCards(connection, transaction);
    }

    private static void SeedCategories(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        // Future category seed data.
    }

    private static void SeedDrinks(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        // Future drink seed data.
    }

    private static void SeedEmployees(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        // Future employee seed data.
    }

    private static void SeedCustomers(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        // Future customer seed data.
    }

    private static void SeedCards(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        // Future loyalty card seed data.
    }
}
