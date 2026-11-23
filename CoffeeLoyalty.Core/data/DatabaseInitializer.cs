using System.IO;

using Microsoft.Data.Sqlite;

namespace CoffeeLoyalty.Core.Data;

// Runs registered SQL schema scripts and seeds initial data in order.
// Safe to call repeatedly without wiping existing records.
public sealed class DatabaseInitializer
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SeedDataFactory _seedDataFactory;

    // Register SQL schema files in dependency order (parent tables before child tables).
    private static readonly string[] SchemaFiles =
    [
        "CategoriesSchema.sql",
        "CustomersSchema.sql",
        "CardsSchema.sql",
        "EmployeesSchema.sql",
        "PurchasesSchema.sql",
        "DrinksSchema.sql",
        "PurchaseItemSchema.sql",
        "UserDrinkSchema.sql",
        "LogsSchema.sql"
    ];
    // Stores the connection factory and seed data helper used during database setup.
    public DatabaseInitializer(
        SqliteConnectionFactory connectionFactory,
        SeedDataFactory seedDataFactory)
    {
        _connectionFactory = connectionFactory
            ?? throw new ArgumentNullException(nameof(connectionFactory));

        _seedDataFactory = seedDataFactory
            ?? throw new ArgumentNullException(nameof(seedDataFactory));
    }

    // Opens the database, executes all registered schemas and seed logic inside one transaction,
    // and rolls back automatically if anything fails.
    public void Initialize()
    {
        // Opens an existing SQLite database or creates coffeeloyalty.db on first run.
        using var connection = _connectionFactory.OpenConnection();

        // Group schema creation and seeding into one atomic transaction.
        using var transaction = connection.BeginTransaction();

        foreach (string schemaFile in SchemaFiles)
        {
            // Load the embedded SQL script for the current schema (including DrinksSchema.sql).
            string sql = LoadSchema(schemaFile);

            // Run the SQL statements within the active transaction.
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        // Upgrade existing LOGS tables before inserting seed data.
        EnsureLogEmployeeIdColumn(connection, transaction);

        // Seed initial data (such as menu items for DRINKS) after schemas are created.
        _seedDataFactory.Seed(connection, transaction);

        // Commit all changes only when every schema and seed step succeeds.
        transaction.Commit();
    }

    // Adds actor tracking to legacy databases without deleting historical records.
    private static void EnsureLogEmployeeIdColumn(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        bool hasEmployeeId = false;

        using (SqliteCommand check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "PRAGMA table_info(LOGS);";

            using SqliteDataReader reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "employeeID", StringComparison.OrdinalIgnoreCase))
                {
                    hasEmployeeId = true;
                    break;
                }
            }
        }

        if (hasEmployeeId)
        {
            return;
        }

        using SqliteCommand alter = connection.CreateCommand();
        alter.Transaction = transaction;
        alter.CommandText = "ALTER TABLE LOGS ADD COLUMN employeeID INTEGER REFERENCES EMPLOYEES(employeeID);";
        alter.ExecuteNonQuery();
    }

    // Reads an embedded SQL script from CoffeeLoyalty.Core/data/schemas by file name.
    private static string LoadSchema(string fileName)
    {
        var assembly = typeof(DatabaseInitializer).Assembly;

        // Matches the lowercase data/schemas folder structure in CoffeeLoyalty.Core.
        string resourceName =
            $"{assembly.GetName().Name}.data.schemas.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException(
                $"Embedded SQL schema not found: {resourceName}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}