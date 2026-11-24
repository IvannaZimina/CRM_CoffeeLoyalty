using CoffeeLoyalty.Core.Data;
using Microsoft.Data.Sqlite;

namespace CoffeeLoyalty.Core.Tests;

internal static class TestDatabasePool
{
    // Clears only the pool belonging to this test database before file deletion.
    public static void Clear(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return;
        }

        using SqliteConnection connection = new SqliteConnectionFactory(databasePath).OpenConnection();
        SqliteConnection.ClearPool(connection);
    }
}
