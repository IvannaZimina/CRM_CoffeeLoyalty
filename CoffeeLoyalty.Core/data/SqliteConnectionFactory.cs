
using Microsoft.Data.Sqlite;

namespace CoffeeLoyalty.Core.Data;

public class SqliteConnectionFactory
{
    private readonly string _databasePath;

    // Uses the application database unless a custom path is provided.
    public SqliteConnectionFactory(string? databasePath = null)
    {
        _databasePath = databasePath ?? DatabaseConfig.DatabasePath;
    }

    // Opens a SQLite connection with foreign keys enabled.
    public SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();

        return connection;
    }
}
