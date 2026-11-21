using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CoffeeLoyalty.Core.Enums;
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
        SeedPurchases(connection, transaction);
        SeedPurchaseItems(connection, transaction);
        SeedUserDrinks(connection, transaction);
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

    // Seeds default Cashier and Manager accounts with hashed passwords without creating duplicates.
    private static void SeedEmployees(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        EmployeeSeedItem[] employees =
        [
            new(
                EmployeeName: "manager",
                Role: EmployeeRole.Manager,
                PasswordHash: HashPassword("manager", "ChangeMe!Manager123"),
                IsActive: 1),
            new(
                EmployeeName: "cashier",
                Role: EmployeeRole.Cashier,
                PasswordHash: HashPassword("cashier", "ChangeMe!Cashier123"),
                IsActive: 1)
        ];

        using var existsCommand = connection.CreateCommand();
        existsCommand.Transaction = transaction;
        existsCommand.CommandText =
            "SELECT COUNT(*) FROM EMPLOYEES WHERE employeeName = $employeeName;";

        var existsNameParam = existsCommand.CreateParameter();
        existsNameParam.ParameterName = "$employeeName";
        existsCommand.Parameters.Add(existsNameParam);

        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT INTO EMPLOYEES (
                employeeName,
                employeeRole,
                employeePassword,
                isActive
            )
            VALUES (
                $employeeName,
                $employeeRole,
                $employeePassword,
                $isActive
            );
            """;

        var insertNameParam = insertCommand.CreateParameter();
        insertNameParam.ParameterName = "$employeeName";
        insertCommand.Parameters.Add(insertNameParam);

        var insertRoleParam = insertCommand.CreateParameter();
        insertRoleParam.ParameterName = "$employeeRole";
        insertCommand.Parameters.Add(insertRoleParam);

        var insertPasswordParam = insertCommand.CreateParameter();
        insertPasswordParam.ParameterName = "$employeePassword";
        insertCommand.Parameters.Add(insertPasswordParam);

        var insertIsActiveParam = insertCommand.CreateParameter();
        insertIsActiveParam.ParameterName = "$isActive";
        insertCommand.Parameters.Add(insertIsActiveParam);

        foreach (EmployeeSeedItem employee in employees)
        {
            existsNameParam.Value = employee.EmployeeName;
            long existingCount = Convert.ToInt64(
                existsCommand.ExecuteScalar(),
                CultureInfo.InvariantCulture);

            if (existingCount > 0)
            {
                continue;
            }

            insertNameParam.Value = employee.EmployeeName;
            insertRoleParam.Value = employee.Role.ToString();
            insertPasswordParam.Value = employee.PasswordHash;
            insertIsActiveParam.Value = employee.IsActive;

            insertCommand.ExecuteNonQuery();
        }
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

    // Seeds sample receipt headers into PURCHASES (with cardID and walk-in cardID = NULL) idempotently.
    private static void SeedPurchases(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        long? employeeId = TryGetEmployeeId(connection, transaction, "cashier")
            ?? TryGetEmployeeId(connection, transaction, "manager");

        if (!employeeId.HasValue)
        {
            return;
        }

        long? activeCardId = TryGetActiveCardId(connection, transaction);

        PurchaseSeedItem[] purchases =
        [
            new(
                CardId: activeCardId,
                EmployeeId: employeeId.Value,
                TotalAmount: 8.00m,
                BonusesSpent: 0,
                BonusesEarned: 8,
                TimeStamp: "2026-01-02T10:00:00Z"),
            new(
                CardId: null,
                EmployeeId: employeeId.Value,
                TotalAmount: 3.00m,
                BonusesSpent: 0,
                BonusesEarned: 0,
                TimeStamp: "2026-01-02T10:15:00Z")
        ];

        using var existsCommand = connection.CreateCommand();
        existsCommand.Transaction = transaction;
        existsCommand.CommandText = """
            SELECT COUNT(*) FROM PURCHASES
            WHERE employeeID = $employeeID AND timeStamp = $timeStamp;
            """;

        var existsEmployeeIdParam = existsCommand.CreateParameter();
        existsEmployeeIdParam.ParameterName = "$employeeID";
        existsCommand.Parameters.Add(existsEmployeeIdParam);

        var existsTimeStampParam = existsCommand.CreateParameter();
        existsTimeStampParam.ParameterName = "$timeStamp";
        existsCommand.Parameters.Add(existsTimeStampParam);

        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT INTO PURCHASES (
                cardID,
                employeeID,
                totalAmount,
                bonusesSpent,
                bonusesEarned,
                timeStamp
            )
            VALUES (
                $cardID,
                $employeeID,
                $totalAmount,
                $bonusesSpent,
                $bonusesEarned,
                $timeStamp
            );
            """;

        var insertCardIdParam = insertCommand.CreateParameter();
        insertCardIdParam.ParameterName = "$cardID";
        insertCommand.Parameters.Add(insertCardIdParam);

        var insertEmployeeIdParam = insertCommand.CreateParameter();
        insertEmployeeIdParam.ParameterName = "$employeeID";
        insertCommand.Parameters.Add(insertEmployeeIdParam);

        var insertTotalAmountParam = insertCommand.CreateParameter();
        insertTotalAmountParam.ParameterName = "$totalAmount";
        insertCommand.Parameters.Add(insertTotalAmountParam);

        var insertBonusesSpentParam = insertCommand.CreateParameter();
        insertBonusesSpentParam.ParameterName = "$bonusesSpent";
        insertCommand.Parameters.Add(insertBonusesSpentParam);

        var insertBonusesEarnedParam = insertCommand.CreateParameter();
        insertBonusesEarnedParam.ParameterName = "$bonusesEarned";
        insertCommand.Parameters.Add(insertBonusesEarnedParam);

        var insertTimeStampParam = insertCommand.CreateParameter();
        insertTimeStampParam.ParameterName = "$timeStamp";
        insertCommand.Parameters.Add(insertTimeStampParam);

        foreach (PurchaseSeedItem purchase in purchases)
        {
            existsEmployeeIdParam.Value = purchase.EmployeeId;
            existsTimeStampParam.Value = purchase.TimeStamp;

            long existingCount = Convert.ToInt64(
                existsCommand.ExecuteScalar(),
                CultureInfo.InvariantCulture);

            if (existingCount > 0)
            {
                continue;
            }

            insertCardIdParam.Value = purchase.CardId.HasValue
                ? purchase.CardId.Value
                : DBNull.Value;
            insertEmployeeIdParam.Value = purchase.EmployeeId;
            insertTotalAmountParam.Value = Convert.ToDouble(
                purchase.TotalAmount,
                CultureInfo.InvariantCulture);
            insertBonusesSpentParam.Value = purchase.BonusesSpent;
            insertBonusesEarnedParam.Value = purchase.BonusesEarned;
            insertTimeStampParam.Value = purchase.TimeStamp;

            insertCommand.ExecuteNonQuery();
        }
    }

    // Seeds sample receipt line items into PURCHASE_ITEM linked to PURCHASES and DRINKS idempotently.
    private static void SeedPurchaseItems(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        long? firstPurchaseId = TryGetPurchaseIdByTimeStamp(
            connection,
            transaction,
            "2026-01-02T10:00:00Z");
        long? secondPurchaseId = TryGetPurchaseIdByTimeStamp(
            connection,
            transaction,
            "2026-01-02T10:15:00Z");

        long? cappuccinoId = TryGetDrinkId(connection, transaction, "Cappuccino");
        long? latteId = TryGetDrinkId(connection, transaction, "Latte");
        long? americanoId = TryGetDrinkId(connection, transaction, "Americano");

        if ((!firstPurchaseId.HasValue || !cappuccinoId.HasValue || !latteId.HasValue) &&
            (!secondPurchaseId.HasValue || !americanoId.HasValue))
        {
            return;
        }

        List<PurchaseItemSeedItem> items = [];
        if (firstPurchaseId.HasValue && cappuccinoId.HasValue && latteId.HasValue)
        {
            items.Add(new(
                PurchaseId: firstPurchaseId.Value,
                DrinkId: cappuccinoId.Value,
                Quantity: 1,
                UnitPrice: 3.80m));
            items.Add(new(
                PurchaseId: firstPurchaseId.Value,
                DrinkId: latteId.Value,
                Quantity: 1,
                UnitPrice: 4.20m));
        }

        if (secondPurchaseId.HasValue && americanoId.HasValue)
        {
            items.Add(new(
                PurchaseId: secondPurchaseId.Value,
                DrinkId: americanoId.Value,
                Quantity: 1,
                UnitPrice: 3.00m));
        }

        using var existsCommand = connection.CreateCommand();
        existsCommand.Transaction = transaction;
        existsCommand.CommandText = """
            SELECT COUNT(*) FROM PURCHASE_ITEM
            WHERE purchaseID = $purchaseID AND drinkID = $drinkID;
            """;

        var existsPurchaseIdParam = existsCommand.CreateParameter();
        existsPurchaseIdParam.ParameterName = "$purchaseID";
        existsCommand.Parameters.Add(existsPurchaseIdParam);

        var existsDrinkIdParam = existsCommand.CreateParameter();
        existsDrinkIdParam.ParameterName = "$drinkID";
        existsCommand.Parameters.Add(existsDrinkIdParam);

        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT INTO PURCHASE_ITEM (
                purchaseID,
                drinkID,
                quantity,
                unitPrice
            )
            VALUES (
                $purchaseID,
                $drinkID,
                $quantity,
                $unitPrice
            );
            """;

        var insertPurchaseIdParam = insertCommand.CreateParameter();
        insertPurchaseIdParam.ParameterName = "$purchaseID";
        insertCommand.Parameters.Add(insertPurchaseIdParam);

        var insertDrinkIdParam = insertCommand.CreateParameter();
        insertDrinkIdParam.ParameterName = "$drinkID";
        insertCommand.Parameters.Add(insertDrinkIdParam);

        var insertQuantityParam = insertCommand.CreateParameter();
        insertQuantityParam.ParameterName = "$quantity";
        insertCommand.Parameters.Add(insertQuantityParam);

        var insertUnitPriceParam = insertCommand.CreateParameter();
        insertUnitPriceParam.ParameterName = "$unitPrice";
        insertCommand.Parameters.Add(insertUnitPriceParam);

        foreach (PurchaseItemSeedItem item in items)
        {
            existsPurchaseIdParam.Value = item.PurchaseId;
            existsDrinkIdParam.Value = item.DrinkId;

            long existingCount = Convert.ToInt64(
                existsCommand.ExecuteScalar(),
                CultureInfo.InvariantCulture);

            if (existingCount > 0)
            {
                continue;
            }

            insertPurchaseIdParam.Value = item.PurchaseId;
            insertDrinkIdParam.Value = item.DrinkId;
            insertQuantityParam.Value = item.Quantity;
            insertUnitPriceParam.Value = Convert.ToDouble(
                item.UnitPrice,
                CultureInfo.InvariantCulture);

            insertCommand.ExecuteNonQuery();
        }
    }

    // Seeds sample per-card drink counters into USER_DRINK linked to CARDS and DRINKS idempotently.
    private static void SeedUserDrinks(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        long? activeCardId = TryGetActiveCardId(connection, transaction);
        long? cappuccinoId = TryGetDrinkId(connection, transaction, "Cappuccino");
        long? latteId = TryGetDrinkId(connection, transaction, "Latte");

        if (!activeCardId.HasValue || (!cappuccinoId.HasValue && !latteId.HasValue))
        {
            return;
        }

        List<UserDrinkSeedItem> userDrinks = [];
        if (cappuccinoId.HasValue)
        {
            userDrinks.Add(new(
                CardId: activeCardId.Value,
                DrinkId: cappuccinoId.Value,
                Quantity: 1));
        }

        if (latteId.HasValue)
        {
            userDrinks.Add(new(
                CardId: activeCardId.Value,
                DrinkId: latteId.Value,
                Quantity: 1));
        }

        using var existsCommand = connection.CreateCommand();
        existsCommand.Transaction = transaction;
        existsCommand.CommandText = """
            SELECT COUNT(*) FROM USER_DRINK
            WHERE cardID = $cardID AND drinkID = $drinkID;
            """;

        var existsCardIdParam = existsCommand.CreateParameter();
        existsCardIdParam.ParameterName = "$cardID";
        existsCommand.Parameters.Add(existsCardIdParam);

        var existsDrinkIdParam = existsCommand.CreateParameter();
        existsDrinkIdParam.ParameterName = "$drinkID";
        existsCommand.Parameters.Add(existsDrinkIdParam);

        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT INTO USER_DRINK (
                cardID,
                drinkID,
                quantity
            )
            VALUES (
                $cardID,
                $drinkID,
                $quantity
            );
            """;

        var insertCardIdParam = insertCommand.CreateParameter();
        insertCardIdParam.ParameterName = "$cardID";
        insertCommand.Parameters.Add(insertCardIdParam);

        var insertDrinkIdParam = insertCommand.CreateParameter();
        insertDrinkIdParam.ParameterName = "$drinkID";
        insertCommand.Parameters.Add(insertDrinkIdParam);

        var insertQuantityParam = insertCommand.CreateParameter();
        insertQuantityParam.ParameterName = "$quantity";
        insertCommand.Parameters.Add(insertQuantityParam);

        foreach (UserDrinkSeedItem userDrink in userDrinks)
        {
            existsCardIdParam.Value = userDrink.CardId;
            existsDrinkIdParam.Value = userDrink.DrinkId;

            long existingCount = Convert.ToInt64(
                existsCommand.ExecuteScalar(),
                CultureInfo.InvariantCulture);

            if (existingCount > 0)
            {
                continue;
            }

            insertCardIdParam.Value = userDrink.CardId;
            insertDrinkIdParam.Value = userDrink.DrinkId;
            insertQuantityParam.Value = userDrink.Quantity;

            insertCommand.ExecuteNonQuery();
        }
    }

    // Hashes a plain-text password with a per-account salt so raw passwords are never stored.
    private static string HashPassword(string salt, string plainPassword)
    {
        byte[] bytes = Encoding.UTF8.GetBytes($"{salt}:{plainPassword}");
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
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
        categoryNameParam.Value = categoryName; // FIX: SQLite requires every parameter to have a value.
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
        customerNameParam.Value = customerName; // FIX: SQLite requires every parameter to have a value.
        command.Parameters.Add(customerNameParam);

        object? result = command.ExecuteScalar();
        return result is null or DBNull
            ? null
            : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    // Resolves an employeeID by employeeName inside the current transaction.
    private static long? TryGetEmployeeId(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string employeeName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT employeeID FROM EMPLOYEES WHERE employeeName = $employeeName ORDER BY employeeID ASC LIMIT 1;";

        var employeeNameParam = command.CreateParameter();
        employeeNameParam.ParameterName = "$employeeName";
        employeeNameParam.Value = employeeName;
        command.Parameters.Add(employeeNameParam);

        object? result = command.ExecuteScalar();
        return result is null or DBNull
            ? null
            : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    // Resolves an active cardID inside the current transaction.
    private static long? TryGetActiveCardId(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT cardID FROM CARDS WHERE isActive = 1 ORDER BY cardID ASC LIMIT 1;";

        object? result = command.ExecuteScalar();
        return result is null or DBNull
            ? null
            : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    // Resolves a purchaseID by timeStamp inside the current transaction.
    private static long? TryGetPurchaseIdByTimeStamp(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string timeStamp)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT purchaseID FROM PURCHASES WHERE timeStamp = $timeStamp ORDER BY purchaseID ASC LIMIT 1;";

        var timeStampParam = command.CreateParameter();
        timeStampParam.ParameterName = "$timeStamp";
        timeStampParam.Value = timeStamp;
        command.Parameters.Add(timeStampParam);

        object? result = command.ExecuteScalar();
        return result is null or DBNull
            ? null
            : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    // Resolves a drinkID by drinkName inside the current transaction.
    private static long? TryGetDrinkId(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string drinkName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT drinkID FROM DRINKS WHERE drinkName = $drinkName ORDER BY drinkID ASC LIMIT 1;";

        var drinkNameParam = command.CreateParameter();
        drinkNameParam.ParameterName = "$drinkName";
        drinkNameParam.Value = drinkName;
        command.Parameters.Add(drinkNameParam);

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

    // Holds seed values for a single staff account in EMPLOYEES.
    private sealed record EmployeeSeedItem(
        string EmployeeName,
        EmployeeRole Role,
        string PasswordHash,
        int IsActive);

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

    // Holds seed values for a single receipt header in PURCHASES.
    private sealed record PurchaseSeedItem(
        long? CardId,
        long EmployeeId,
        decimal TotalAmount,
        int BonusesSpent,
        int BonusesEarned,
        string TimeStamp);

    // Holds seed values for a single receipt line item in PURCHASE_ITEM.
    private sealed record PurchaseItemSeedItem(
        long PurchaseId,
        long DrinkId,
        int Quantity,
        decimal UnitPrice);

    // Holds seed values for a single per-card drink counter in USER_DRINK.
    private sealed record UserDrinkSeedItem(
        long CardId,
        long DrinkId,
        int Quantity);
}