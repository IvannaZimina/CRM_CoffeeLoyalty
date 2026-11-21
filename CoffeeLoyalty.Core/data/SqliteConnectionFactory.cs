
using Microsoft.Data.Sqlite;

namespace CoffeeLoyalty.Core.Data;

/// <summary>
/// Creates and opens SQLite database connections.
/// </summary>
public class SqliteConnectionFactory
{
    /// <summary>
    /// Opens a connection with foreign key validation enabled.
    /// </summary>
    public SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabaseConfig.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        };

        var connection = new SqliteConnection(builder.ToString());

        connection.Open();

        return connection;
    }
}
