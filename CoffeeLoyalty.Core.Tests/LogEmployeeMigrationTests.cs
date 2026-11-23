using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoffeeLoyalty.Core.Tests;

public class LogEmployeeMigrationTests
{
    [Fact]
    public async Task FreshDatabase_InitializesTwiceWithEmployeeIdColumn()
    {
        await WithTemporaryDatabaseAsync(async factory =>
        {
            DatabaseInitializer initializer = new(factory, new SeedDataFactory());
            initializer.Initialize();
            initializer.Initialize();

            await using SqliteConnection connection = factory.OpenConnection();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(LOGS);";

            int matches = 0;
            await using (SqliteDataReader reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    if (reader.GetString(1) == "employeeID")
                    {
                        matches++;
                    }
                }
            }

            Assert.Equal(1, matches);
            Assert.NotEmpty(await new LogRepository(factory).GetAllAsync());
        });
    }

    [Fact]
    public async Task LegacyDatabase_MigratesTwiceAndPreservesOldHistory()
    {
        await WithTemporaryDatabaseAsync(async factory =>
        {
            // Simulates LOGS created by the previous application version.
            await using (SqliteConnection connection = factory.OpenConnection())
            {
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE LOGS (
                        logID INTEGER PRIMARY KEY AUTOINCREMENT,
                        objectID INTEGER,
                        objectType TEXT NOT NULL,
                        actionType TEXT NOT NULL,
                        description TEXT NOT NULL,
                        timeStamp TEXT NOT NULL
                    );
                    INSERT INTO LOGS (objectID, objectType, actionType, description, timeStamp)
                    VALUES (15, 'DRINKS', 'DrinkUpdated', 'Legacy event', '2026-01-02T10:00:00Z');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            DatabaseInitializer initializer = new(factory, new SeedDataFactory());
            initializer.Initialize();
            initializer.Initialize();

            LogRepository repository = new(factory);
            IReadOnlyList<LogEntry> logs = await repository.GetAllAsync();
            LogEntry legacy = Assert.Single(
                logs, x => x.Description == "Legacy event");
            Assert.Equal(15, legacy.ObjectId);
            Assert.Null(legacy.EmployeeId);

            await using SqliteConnection verifyConnection = factory.OpenConnection();
            await using SqliteCommand verify = verifyConnection.CreateCommand();
            verify.CommandText = "SELECT COUNT(*) FROM LOGS WHERE description = 'Legacy event';";
            Assert.Equal(1L, Convert.ToInt64(await verify.ExecuteScalarAsync()));

            // New data can store an employee independently from the affected object.
            verify.CommandText = "SELECT employeeID FROM EMPLOYEES LIMIT 1;";
            int actorId = Convert.ToInt32(await verify.ExecuteScalarAsync());
            Assert.True(actorId > 0);

            LogEntry next = new("DRINKS", "DrinkUpdated", "New event",
                objectId: 15, employeeId: actorId);
                await repository.AddAsync(next);
            LogEntry saved = Assert.Single(
                await repository.GetAllAsync(),
                x => x.Description == "New event");
            Assert.Equal(actorId, saved.EmployeeId);
            Assert.Equal(15, saved.ObjectId);
        });
    }

    private static async Task WithTemporaryDatabaseAsync(
        Func<SqliteConnectionFactory, Task> test)
    {
        string path = Path.Combine(Path.GetTempPath(),
            $"coffeeloyalty_log_migration_{Guid.NewGuid():N}.db");
        try
        {
            await test(new SqliteConnectionFactory(path));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
