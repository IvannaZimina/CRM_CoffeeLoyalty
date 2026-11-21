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

    // Seeds sample active and inactive/blocked customers linked to CATEGORIES without duplicates.
    private static void SeedCustomers(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        long? basicCategoryId = TryGetCategoryId(connection, transaction, "Basic");
        long? silverCategoryId = TryGetCategoryId(connection, transaction, "Silver");

        if (!basicCategoryId.HasValue)
        {
            return;
        }

        CustomerSeedItem[] customers =
        [
            new(
                CustomerName: "Anna Tamm",
                CategoryId: basicCategoryId.Value,
                BonusBalance: 15,
                BirthDate: "1996-05-14",
                IsActive: 1,
                TimeStamp: "2026-01-01T09:00:00Z"),
            new(
                CustomerName: "Mark Kask",
                CategoryId: silverCategoryId ?? basicCategoryId.Value,
                BonusBalance: 40,
                BirthDate: null,
                IsActive: 1,
                TimeStamp: "2026-01-01T09:10:00Z"),
            new(
                CustomerName: "Liis Saar",
                CategoryId: basicCategoryId.Value,
                BonusBalance: 0,
                BirthDate: "1991-11-20",
                IsActive: 0,
                TimeStamp: "2026-01-01T09:20:00Z")
        ];

        using var existsCommand = connection.CreateCommand();
        existsCommand.Transaction = transaction;
        existsCommand.CommandText = """
            SELECT COUNT(*) FROM CUSTOMERS
            WHERE customerName = $customerName AND timeStamp = $timeStamp;
            """;

        var existsNameParam = existsCommand.CreateParameter();
        existsNameParam.ParameterName = "$customerName";
        existsCommand.Parameters.Add(existsNameParam);

        var existsStampParam = existsCommand.CreateParameter();
        existsStampParam.ParameterName = "$timeStamp";
        existsCommand.Parameters.Add(existsStampParam);

        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT INTO CUSTOMERS (
                categoryID,
                customerName,
                bonusBalance,
                birthDate,
                isActive,
                timeStamp
            )
            VALUES (
                $categoryID,
                $customerName,
                $bonusBalance,
                $birthDate,
                $isActive,
                $timeStamp
            );
            """;

        var insertCategoryIdParam = insertCommand.CreateParameter();
        insertCategoryIdParam.ParameterName = "$categoryID";
        insertCommand.Parameters.Add(insertCategoryIdParam);

        var insertCustomerNameParam = insertCommand.CreateParameter();
        insertCustomerNameParam.ParameterName = "$customerName";
        insertCommand.Parameters.Add(insertCustomerNameParam);

        var insertBonusBalanceParam = insertCommand.CreateParameter();
        insertBonusBalanceParam.ParameterName = "$bonusBalance";
        insertCommand.Parameters.Add(insertBonusBalanceParam);

        var insertBirthDateParam = insertCommand.CreateParameter();
        insertBirthDateParam.ParameterName = "$birthDate";
        insertCommand.Parameters.Add(insertBirthDateParam);

        var insertIsActiveParam = insertCommand.CreateParameter();
        insertIsActiveParam.ParameterName = "$isActive";
        insertCommand.Parameters.Add(insertIsActiveParam);

        var insertTimeStampParam = insertCommand.CreateParameter();
        insertTimeStampParam.ParameterName = "$timeStamp";
        insertCommand.Parameters.Add(insertTimeStampParam);

        foreach (CustomerSeedItem customer in customers)
        {
            existsNameParam.Value = customer.CustomerName;
            existsStampParam.Value = customer.TimeStamp;

            long existingCount = Convert.ToInt64(
                existsCommand.ExecuteScalar(),
                CultureInfo.InvariantCulture);

            if (existingCount > 0)
            {
                continue;
            }

            insertCategoryIdParam.Value = customer.CategoryId;
            insertCustomerNameParam.Value = customer.CustomerName;
            insertBonusBalanceParam.Value = customer.BonusBalance;
            insertBirthDateParam.Value = (object?)customer.BirthDate ?? DBNull.Value;
            insertIsActiveParam.Value = customer.IsActive;
            insertTimeStampParam.Value = customer.TimeStamp;

            insertCommand.ExecuteNonQuery();
        }
    }

    // Seeds sample loyalty cards linked to CUSTOMERS without creating duplicates across repeated runs.
    private static void SeedCards(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        long? annaCustomerId = TryGetCustomerId(connection, transaction, "Anna Tamm");
        long? markCustomerId = TryGetCustomerId(connection, transaction, "Mark Kask");

        if (!annaCustomerId.HasValue && !markCustomerId.HasValue)
        {
            return;
        }

        List<CardSeedItem> cards = [];
        if (annaCustomerId.HasValue)
        {
            // Support multiple cards per customer (1:N), including an inactive/blocked card (isActive = 0).
            cards.Add(new(CustomerId: annaCustomerId.Value, IsActive: 1));
            cards.Add(new(CustomerId: annaCustomerId.Value, IsActive: 0));
        }

        if (markCustomerId.HasValue)
        {
            cards.Add(new(CustomerId: markCustomerId.Value, IsActive: 1));
        }

        using var existsCommand = connection.CreateCommand();
        existsCommand.Transaction = transaction;
        existsCommand.CommandText = """
            SELECT COUNT(*) FROM CARDS
            WHERE customerID = $customerID AND isActive = $isActive;
            """;

        var existsCustomerIdParam = existsCommand.CreateParameter();
        existsCustomerIdParam.ParameterName = "$customerID";
        existsCommand.Parameters.Add(existsCustomerIdParam);

        var existsIsActiveParam = existsCommand.CreateParameter();
        existsIsActiveParam.ParameterName = "$isActive";
        existsCommand.Parameters.Add(existsIsActiveParam);

        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT INTO CARDS (customerID, isActive)
            VALUES ($customerID, $isActive);
            """;

        var insertCustomerIdParam = insertCommand.CreateParameter();
        insertCustomerIdParam.ParameterName = "$customerID";
        insertCommand.Parameters.Add(insertCustomerIdParam);

        var insertIsActiveParam = insertCommand.CreateParameter();
        insertIsActiveParam.ParameterName = "$isActive";
        insertCommand.Parameters.Add(insertIsActiveParam);

        foreach (CardSeedItem card in cards)
        {
            existsCustomerIdParam.Value = card.CustomerId;
            existsIsActiveParam.Value = card.IsActive;

            long existingCount = Convert.ToInt64(
                existsCommand.ExecuteScalar(),
                CultureInfo.InvariantCulture);

            if (existingCount > 0)
            {
                continue;
            }

            insertCustomerIdParam.Value = card.CustomerId;
            insertIsActiveParam.Value = card.IsActive;

            insertCommand.ExecuteNonQuery();
        }
    }

    // Resolves a categoryID by categoryName inside the current transaction.
    private static long? TryGetCategoryId(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string categoryName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT categoryID FROM CATEGORIES WHERE categoryName = $categoryName LIMIT 1;";

        var categoryNameParam = command.CreateParameter();
        categoryNameParam.ParameterName = "$categoryName";
        categoryNameParam.Value = categoryName;
        command.Parameters.Add(categoryNameParam);

        object? result = command.ExecuteScalar();
        return result is null or DBNull
            ? null
            : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    // Resolves a customerID by customerName inside the current transaction.
    private static long? TryGetCustomerId(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string customerName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT customerID FROM CUSTOMERS WHERE customerName = $customerName ORDER BY customerID ASC LIMIT 1;";

        var customerNameParam = command.CreateParameter();
        customerNameParam.ParameterName = "$customerName";
        customerNameParam.Value = customerName;
        command.Parameters.Add(customerNameParam);

        object? result = command.ExecuteScalar();
        return result is null or DBNull
            ? null
            : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    // Holds seed values for a single menu item in DRINKS.
    private sealed record DrinkSeedItem(
        string DrinkName,
        string DrinkType,
        decimal DrinkPrice,
        int IsActive,
        string? DrinkImageLink);

    // Holds seed values for a single customer row in CUSTOMERS.
    private sealed record CustomerSeedItem(
        string CustomerName,
        long CategoryId,
        int BonusBalance,
        string? BirthDate,
        int IsActive,
        string TimeStamp);

    // Holds seed values for a single loyalty card row in CARDS.
    private sealed record CardSeedItem(
        long CustomerId,
        int IsActive);
}