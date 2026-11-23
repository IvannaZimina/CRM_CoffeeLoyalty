
using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoffeeLoyalty.Core.Tests;

public class LogRepositoryTests
{
    private static readonly DateTime TestTime =
        new(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AddSystemLog_PreservesNullableObjectId()
    {
        await WithDatabaseAsync(async (repository, factory) =>
        {
            LogEntry entry = new(
                "System", "LogStartup", "Application started.",
                timeStamp: TestTime);

            int id = await repository.AddAsync(entry);
            IReadOnlyList<LogEntry> all = await repository.GetAllAsync();

            Assert.True(id > 0);
            Assert.Equal(id, entry.Id);

            LogEntry saved = Assert.Single(all);

            Assert.Equal(id, saved.Id);
            Assert.Null(saved.ObjectId);
            Assert.Equal("System", saved.ObjectType);
            Assert.Equal("LogStartup", saved.ActionType);
            Assert.Equal("Application started.", saved.Description);
            Assert.Equal(TestTime, saved.TimeStamp);

            await using SqliteConnection connection =
                factory.OpenConnection();

            await using SqliteCommand command = connection.CreateCommand();

            command.CommandText = """
                SELECT COUNT(*)
                FROM LOGS
                WHERE logID = @id AND objectID IS NULL;
                """;

            command.Parameters.Add("@id", SqliteType.Integer).Value = id;

            Assert.Equal(
                1L,
                Convert.ToInt64(await command.ExecuteScalarAsync()));
        });
    }

    [Fact]
    public async Task AddRelatedLog_PreservesObjectIdAndSpecialCharacters()
    {
        await WithDatabaseAsync(async (repository, factory) =>
        {
            LogEntry entry = new(
                " PURCHASES ",
                " LogPurchase ",
                "O'Brien's order completed.",
                objectId: 25,
                timeStamp: TestTime);

            int id = await repository.AddAsync(entry);
            IReadOnlyList<LogEntry> saved =
                await repository.GetByActionTypeAsync("LogPurchase");

            LogEntry log = Assert.Single(saved);

            Assert.Equal(id, log.Id);
            Assert.Equal(25, log.ObjectId);
            Assert.Equal("PURCHASES", log.ObjectType);
            Assert.Equal("LogPurchase", log.ActionType);
            Assert.Equal(
                "O'Brien's order completed.",
                log.Description);
        });
    }

    [Fact]
    public async Task GetAll_ReturnsNewestLogsFirst()
    {
        await WithDatabaseAsync(async (repository, factory) =>
        {
            int oldest = await repository.AddAsync(
                new LogEntry("System", "LogFirst", "First",
                    timeStamp: TestTime.AddMinutes(-10)));

            int newest = await repository.AddAsync(
                new LogEntry("System", "LogLast", "Last",
                    timeStamp: TestTime.AddMinutes(10)));

            int middle = await repository.AddAsync(
                new LogEntry("System", "LogMiddle", "Middle",
                    timeStamp: TestTime));

            IReadOnlyList<LogEntry> all = await repository.GetAllAsync();

            Assert.Equal(
                new[] { newest, middle, oldest },
                all.Select(log => log.Id).ToArray());
        });
    }

    [Fact]
    public async Task GetByDateRange_IncludesBoundsAndLegacySeedFormat()
    {
        await WithDatabaseAsync(async (repository, factory) =>
        {
            await repository.AddAsync(
                new LogEntry("System", "LogBefore", "Before",
                    timeStamp: TestTime.AddMinutes(-1)));

            int atStart = await repository.AddAsync(
                new LogEntry("System", "LogStart", "Start",
                    timeStamp: TestTime));

            int atEnd = await repository.AddAsync(
                new LogEntry("System", "LogEnd", "End",
                    timeStamp: TestTime.AddMinutes(1)));

            await repository.AddAsync(
                new LogEntry("System", "LogAfter", "After",
                    timeStamp: TestTime.AddMinutes(2)));

            // Simulates an existing log written by SeedDataFactory.
            await using (SqliteConnection connection =
                factory.OpenConnection())
            {
                await using SqliteCommand command =
                    connection.CreateCommand();

                command.CommandText = """
                    INSERT INTO LOGS (
                        objectID, objectType, actionType,
                        description, timeStamp
                    )
                    VALUES (
                        NULL, 'System', 'LogStartup',
                        'Legacy seed', '2026-01-02T10:00:00Z'
                    );
                    """;

                await command.ExecuteNonQueryAsync();
            }

            IReadOnlyList<LogEntry> result =
                await repository.GetByDateRangeAsync(
                    TestTime,
                    TestTime.AddMinutes(1));

            Assert.Equal(3, result.Count);
            Assert.Contains(result, log => log.Id == atStart);
            Assert.Contains(result, log => log.Id == atEnd);
            Assert.Contains(result,
                log => log.Description == "Legacy seed");
        });
    }

    [Fact]
    public async Task GetByActionType_ReturnsOnlyMatchingEvents()
    {
        await WithDatabaseAsync(async (repository, factory) =>
        {
            int purchaseId = await repository.AddAsync(
                new LogEntry("PURCHASES", "LogPurchase", "Purchase",
                    timeStamp: TestTime));

            await repository.AddAsync(
                new LogEntry("System", "ErrorLogin", "Login failed",
                    timeStamp: TestTime));

            await repository.AddAsync(
                new LogEntry("PURCHASES", "LogPurchaseFailed", "Failed",
                    timeStamp: TestTime));

            IReadOnlyList<LogEntry> result =
                await repository.GetByActionTypeAsync(" LogPurchase ");

            LogEntry saved = Assert.Single(result);

            Assert.Equal(purchaseId, saved.Id);

            Assert.Empty(await repository.GetByActionTypeAsync(
                "LogPurchase' OR 1=1 --"));
        });
    }

    [Fact]
    public async Task EmptyQueries_ReturnEmptyCollections()
    {
        await WithDatabaseAsync(async (repository, factory) =>
        {
            Assert.Empty(await repository.GetAllAsync());

            Assert.Empty(await repository.GetByActionTypeAsync(
                "NotFound"));

            Assert.Empty(await repository.GetByActionTypeAsync(
                " "));

            Assert.Empty(await repository.GetByDateRangeAsync(
                TestTime.AddDays(-1),
                TestTime.AddDays(1)));
        });
    }

    [Fact]
    public async Task InvalidDateRange_IsRejected()
    {
        await WithDatabaseAsync(async (repository, factory) =>
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => repository.GetByDateRangeAsync(
                    TestTime.AddDays(1),
                    TestTime));
        });
    }

    [Fact]
    public async Task AddLog_PreservesDifferentEmployeeAndObjectIds()
    {
        await WithDatabaseAsync(async (repository, factory) =>
        {
            await using (SqliteConnection connection = factory.OpenConnection())
            {
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO EMPLOYEES (employeeID, employeeName, employeeRole, employeePassword, isActive)
                    VALUES (2, 'Test manager', 'Manager', 'test-only-hash', 1);
                    """;
                await command.ExecuteNonQueryAsync();
            }

            LogEntry entry = new("DRINKS", "DrinkUpdated", "Updated drink.",
                objectId: 15, timeStamp: TestTime, employeeId: 2);

            int id = await repository.AddAsync(entry);
            LogEntry saved = Assert.Single(await repository.GetAllAsync());

            Assert.Equal(id, saved.Id);
            Assert.Equal(2, saved.EmployeeId);
            Assert.Equal(15, saved.ObjectId);
            Assert.Equal(2, Assert.Single(await repository.GetByActionTypeAsync("DrinkUpdated")).EmployeeId);
        });
    }

    [Fact]
    public async Task AddLog_WithUnknownEmployee_FailsForeignKeyConstraint()
    {
        await WithDatabaseAsync(async (repository, factory) =>
        {
            LogEntry entry = new("System", "LogTest", "Invalid actor",
                timeStamp: TestTime, employeeId: 999999);

            SqliteException error = await Assert.ThrowsAsync<SqliteException>(
                () => repository.AddAsync(entry));

            Assert.Equal(19, error.SqliteErrorCode);
            Assert.Empty(await repository.GetAllAsync());
        });
    }

    // Creates an isolated SQLite database with the real employee and log schemas.
    private static async Task WithDatabaseAsync(
        Func<LogRepository, SqliteConnectionFactory, Task> test)
    {
        string databasePath = Path.Combine(
            Path.GetTempPath(),
            $"coffeeloyalty_log_{Guid.NewGuid():N}.db");

        try
        {
            SqliteConnectionFactory factory = new(databasePath);

            await using (SqliteConnection connection =
                factory.OpenConnection())
            {
                foreach (string schemaName in new[] { "EmployeesSchema.sql", "LogsSchema.sql" })
                {
                    await using Stream schemaStream =
                        typeof(DatabaseInitializer).Assembly
                            .GetManifestResourceStream(
                                $"CoffeeLoyalty.Core.data.schemas.{schemaName}")
                        ?? throw new InvalidOperationException();

                    using StreamReader reader = new(schemaStream);

                    await using SqliteCommand command =
                        connection.CreateCommand();

                    command.CommandText = await reader.ReadToEndAsync();
                    await command.ExecuteNonQueryAsync();
                }
            }

            LogRepository repository = new(factory);

            await test(repository, factory);
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}
