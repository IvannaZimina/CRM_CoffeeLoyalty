
using System.IO;

namespace CoffeeLoyalty.Core.Data;

/// <summary>
/// Creates the database tables by running SQL files in the chosen order.
/// The same method can be called again without deleting existing data.
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SeedDataFactory _seedDataFactory;

    // Add real SQL files here later, putting parent tables before child tables.
    // TEST ONLY: remove TestSchema.sql from the list before the final commit.
    private static readonly string[] SchemaFiles = ["TestSchema.sql"];

    /// <summary>
    /// Receives the factory for database connections and a helper for sample data.
    /// This keeps database setup in one place.
    /// </summary>
    public DatabaseInitializer(
        SqliteConnectionFactory connectionFactory,
        SeedDataFactory seedDataFactory)
    {
        _connectionFactory = connectionFactory
            ?? throw new ArgumentNullException(nameof(connectionFactory));

        _seedDataFactory = seedDataFactory
            ?? throw new ArgumentNullException(nameof(seedDataFactory));
    }

    /// <summary>
    /// Opens the database, runs registered SQL scripts, and saves the changes.
    /// If any script fails, the transaction will be rolled back.
    /// </summary>
    public void Initialize()
    {
        // Creates coffeeloyalty.db automatically on the first run.
        using var connection = _connectionFactory.OpenConnection();

        // Keep the SQL scripts and initial data together in one transaction.
        using var transaction = connection.BeginTransaction();

        foreach (string schemaFile in SchemaFiles)
        {
            // Read a SQL script stored inside the compiled Core library.
            string sql = LoadSchema(schemaFile);

            // Execute the script using our active database transaction.
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        // Nothing is inserted yet; this helper is for future table tasks.
        _seedDataFactory.Seed(connection, transaction);

        // Save changes only when every step succeeded.
        transaction.Commit();
    }

    /// <summary>
    /// Finds a named SQL script in the Core assembly and reads its content.
    /// Gives a clear error when the requested file is missing.
    /// </summary>
    private static string LoadSchema(string fileName)
    {
        var assembly = typeof(DatabaseInitializer).Assembly;

        // This path matches the existing lowercase folders: data/schemas.
        string resourceName =
            $"{assembly.GetName().Name}.data.schemas.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException(
                $"Embedded SQL schema not found: {resourceName}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
