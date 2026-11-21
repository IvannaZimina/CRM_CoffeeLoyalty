
using System.IO;
using Microsoft.Data.Sqlite;

namespace CoffeeLoyalty.Core.Data;

/// <summary>
/// Creates the database schema and initializes seed data.
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SeedDataFactory _seedDataFactory;

    // Add SQL files here in foreign-key dependency order.
    private static readonly string[] SchemaFiles =
    [
    ];

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
    /// Initializes the database without deleting existing data.
    /// </summary>
    public void Initialize()
    {
        using var connection = _connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        foreach (string schemaFile in SchemaFiles)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = LoadSchema(schemaFile);
            command.ExecuteNonQuery();
        }

        _seedDataFactory.Seed(connection, transaction);

        transaction.Commit();
    }

    /// <summary>
    /// Loads an embedded SQL schema file.
    /// </summary>
    private static string LoadSchema(string fileName)
    {
        var assembly = typeof(DatabaseInitializer).Assembly;

        string resourceName =
            $"{assembly.GetName().Name}.data.schemas.{fileName}";

        using var stream = assembly.GetManifestResourceStream(
            resourceName)
            ?? throw new FileNotFoundException(
                $"Embedded SQL schema not found: {resourceName}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
