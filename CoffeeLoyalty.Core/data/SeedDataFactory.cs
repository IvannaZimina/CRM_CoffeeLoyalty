using System.Globalization;
using Microsoft.Data.Sqlite;

namespace CoffeeLoyalty.Core.Data;

// Inserts default reference and menu data into the SQLite database after schemas are created.
public sealed class SeedDataFactory
{
    // Runs all table seeders inside the caller's transaction in dependency order.
    public void Seed(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        // Ensure valid database connection and active transaction before inserting data.
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        // Seed tables in dependency order using duplicate-safe logic so repeated runs stay idempotent.
        SeedCategories(connection, transaction);
        SeedDrinks(connection, transaction);
        SeedEmployees(connection, transaction);
        SeedCustomers(connection, transaction);
        SeedCards(connection, transaction);
    }

    // Seeds loyalty or menu categories once future category tasks are implemented.
    private static void SeedCategories(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        // Add default tiers only if they do not already exist.
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        // Rates and thresholds remain zero until the loyalty rules are agreed.
        command.CommandText = """
        INSERT INTO CATEGORIES (
            categoryName,
            bonusAccrualRate,
            upgradeThreshold
        )
        VALUES
            ('Basic', 0, 0),
            ('Silver', 0, 0),
            ('Gold', 0, 0)
        ON CONFLICT(categoryName) DO NOTHING;
        """;

        command.ExecuteNonQuery();
    }

    // Seeds initial menu items into DRINKS without creating duplicates across repeated runs.
    private static void SeedDrinks(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        // Relative asset paths follow CoffeeLoyalty.WPF/assets/images/drinks/ and file names from task #80.
        // Fallback placeholder path when no image is set or file is missing: assets/images/drinks/default_drink.png.
        DrinkSeedItem[] drinks =
        [
            new("Cappuccino", "Kohv", 3.80m, 1, "assets/images/drinks/cappuccino.png"),
        new("Latte", "Kohv", 4.20m, 1, "assets/images/drinks/latte.png"),
        new("Americano", "Kohv", 3.00m, 1, "assets/images/drinks/americano.png"),
        new("Tee", "Tee", 2.80m, 1, "assets/images/drinks/tea.png"),
        new("Mocha", "Kohv", 4.50m, 1, "assets/images/drinks/mocha.png")
        ];

        // Check whether a drink with this name already exists so repeated runs stay idempotent.
        using var existsCommand = connection.CreateCommand();
        existsCommand.Transaction = transaction;
        existsCommand.CommandText =
            "SELECT COUNT(*) FROM DRINKS WHERE drinkName = $drinkName;";

        var existsNameParam = existsCommand.CreateParameter();
        existsNameParam.ParameterName = "$drinkName";
        existsCommand.Parameters.Add(existsNameParam);

        // Insert a drink row only when no row with the same drinkName exists yet.
        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            """
        INSERT INTO DRINKS (drinkName, drinkPrice, isActive, drinkType, drinkImageLink)
        VALUES ($drinkName, $drinkPrice, $isActive, $drinkType, $drinkImageLink);
        """;

        var insertNameParam = insertCommand.CreateParameter();
        insertNameParam.ParameterName = "$drinkName";
        insertCommand.Parameters.Add(insertNameParam);

        var insertPriceParam = insertCommand.CreateParameter();
        insertPriceParam.ParameterName = "$drinkPrice";
        insertCommand.Parameters.Add(insertPriceParam);

        var insertIsActiveParam = insertCommand.CreateParameter();
        insertIsActiveParam.ParameterName = "$isActive";
        insertCommand.Parameters.Add(insertIsActiveParam);

        var insertTypeParam = insertCommand.CreateParameter();
        insertTypeParam.ParameterName = "$drinkType";
        insertCommand.Parameters.Add(insertTypeParam);

        var insertImageParam = insertCommand.CreateParameter();
        insertImageParam.ParameterName = "$drinkImageLink";
        insertCommand.Parameters.Add(insertImageParam);

        foreach (DrinkSeedItem drink in drinks)
        {
            existsNameParam.Value = drink.DrinkName;
            long existingCount = Convert.ToInt64(
                existsCommand.ExecuteScalar(),
                CultureInfo.InvariantCulture);

            // Skip rows that were already seeded earlier.
            if (existingCount > 0)
            {
                continue;
            }

            insertNameParam.Value = drink.DrinkName;
            insertPriceParam.Value = Convert.ToDouble(drink.DrinkPrice, CultureInfo.InvariantCulture);
            insertIsActiveParam.Value = drink.IsActive;
            insertTypeParam.Value = drink.DrinkType;
            insertImageParam.Value = (object?)drink.DrinkImageLink ?? DBNull.Value;

            insertCommand.ExecuteNonQuery();
        }
    }

    // Seeds staff accounts once employee tasks are implemented.
    private static void SeedEmployees(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        // Placeholder until employee seeding is added.
    }

    // Seeds sample loyalty customers once customer tasks are implemented.
    private static void SeedCustomers(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        // Placeholder until customer seeding is added.
    }

    // Seeds loyalty cards once card tasks are implemented.
    private static void SeedCards(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        // Placeholder until loyalty card seeding is added.
    }

    // Holds seed values for a single menu item in DRINKS.
    private sealed record DrinkSeedItem(
        string DrinkName,
        string DrinkType,
        decimal DrinkPrice,
        int IsActive,
        string? DrinkImageLink);
}