using System.Data;
using CoffeeLoyalty.Core.Data;
using Microsoft.Data.Sqlite;

namespace CoffeeLoyalty.Core.Repositories;

// Abstract base class that eliminates code duplication when working with SQLite connections and transactions.
public abstract class BaseRepository
{
    // Connection factory used to open SQLite connections with foreign keys enabled.
    private readonly SqliteConnectionFactory _connectionFactory;

    // Stores the connection factory and rejects null so repositories always have a valid DB source.
    protected BaseRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory
            ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    // Opens a connection via SqliteConnectionFactory and ensures it is open before returning.
    protected SqliteConnection OpenConnection()
    {
        SqliteConnection connection = _connectionFactory.OpenConnection();

        // Open explicitly only if the connection is not already open.
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        return connection;
    }

    // Runs database operations inside a transaction: commits on success, rolls back on error.
    protected async Task<T> ExecuteInTransactionAsync<T>(
        Func<SqliteConnection, SqliteTransaction, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        // Open a connection and start a transaction for the group of SQL commands.
        await using SqliteConnection connection = OpenConnection();
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Execute repository queries using this connection and transaction.
            T result = await action(connection, transaction, cancellationToken);

            // Commit all changes once all commands finish without errors.
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            // Roll back changes automatically if an error occurs.
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}