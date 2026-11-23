
using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.Repositories;
using CoffeeLoyalty.Core.resources;
using CoffeeLoyalty.Core.Validation;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoffeeLoyalty.Core.Tests;

public class EmployeeRepositoryTests
{
    [Fact]
    public async Task AddAndGetEmployee_ReturnsSavedValues()
    {
        await WithDatabaseAsync(async repository =>
        {
            string hash = PasswordHasher.HashPassword("TestPassword!2026");
            Employee employee = new("O'Brien", UserRole.Manager, hash);

            int id = await repository.AddAsync(employee);
            Employee? saved = await repository.GetByIdAsync(id);
            IReadOnlyList<Employee> all = await repository.GetAllAsync();

            Assert.True(id > 0);
            Assert.Equal(id, employee.Id);
            Assert.NotNull(saved);
            Assert.Equal("O'Brien", saved.EmployeeName);
            Assert.Equal(UserRole.Manager, saved.EmployeeRole);
            Assert.Equal(hash, saved.EmployeePassword);
            Assert.True(saved.IsActive);
            Assert.True(PasswordHasher.VerifyPassword(
                "TestPassword!2026", saved.EmployeePassword));
            Assert.Single(all);
        });
    }

    [Fact]
    public async Task GetByName_ReturnsInactiveEmployee()
    {
        await WithDatabaseAsync(async repository =>
        {
            Employee employee = new(
                "cashier_test",
                UserRole.Cashier,
                PasswordHasher.HashPassword("TestPassword!2026"),
                isActive: false);

            await repository.AddAsync(employee);

            Employee? saved =
                await repository.GetByNameAsync(" cashier_test ");

            Assert.NotNull(saved);
            Assert.Equal(employee.Id, saved.Id);
            Assert.Equal(UserRole.Cashier, saved.EmployeeRole);
            Assert.False(saved.IsActive);
        });
    }

    [Fact]
    public async Task UpdateEmployee_PersistsChanges()
    {
        await WithDatabaseAsync(async repository =>
        {
            Employee employee = new(
                "original",
                UserRole.Cashier,
                PasswordHasher.HashPassword("OriginalPassword!2026"));

            await repository.AddAsync(employee);

            string newHash =
                PasswordHasher.HashPassword("UpdatedPassword!2026");

            employee.EmployeeName = "updated";
            employee.EmployeeRole = UserRole.Manager;
            employee.EmployeePassword = newHash;
            employee.IsActive = false;

            bool updated = await repository.UpdateAsync(employee);
            Employee? saved = await repository.GetByIdAsync(employee.Id);

            Assert.True(updated);
            Assert.NotNull(saved);
            Assert.Equal("updated", saved.EmployeeName);
            Assert.Equal(UserRole.Manager, saved.EmployeeRole);
            Assert.Equal(newHash, saved.EmployeePassword);
            Assert.False(saved.IsActive);
        });
    }

    [Fact]
    public async Task DuplicateEmployeeName_IsRejected()
    {
        await WithDatabaseAsync(async repository =>
        {
            string hash = PasswordHasher.HashPassword("TestPassword!2026");

            await repository.AddAsync(
                new Employee("duplicate", UserRole.Cashier, hash));

            SqliteException exception = await Assert.ThrowsAsync<SqliteException>(
                () => repository.AddAsync(
                    new Employee("duplicate", UserRole.Manager, hash)));

            Assert.Equal(19, exception.SqliteErrorCode);
            Assert.Single(await repository.GetAllAsync());
        });
    }

    [Fact]
    public async Task MissingEmployee_ReturnsNullOrFalse()
    {
        await WithDatabaseAsync(async repository =>
        {
            Assert.Null(await repository.GetByIdAsync(999));
            Assert.Null(await repository.GetByIdAsync(0));
            Assert.Null(await repository.GetByNameAsync(" "));
            Assert.Null(await repository.GetByNameAsync("unknown"));

            Employee missing = new(
                "unknown",
                UserRole.Cashier,
                PasswordHasher.HashPassword("TestPassword!2026"))
            {
                Id = 999
            };

            Assert.False(await repository.UpdateAsync(missing));
        });
    }

    [Fact]
    public async Task MissingPasswordHash_IsRejected()
    {
        await WithDatabaseAsync(async repository =>
        {
            Employee employee = new("no_hash", UserRole.Cashier);

            ArgumentException exception =
                await Assert.ThrowsAsync<ArgumentException>(
                    () => repository.AddAsync(employee));

            Assert.Contains(
                CoreResources.ValEmployeePasswordHashRequired,
                exception.Message);

            Assert.Empty(await repository.GetAllAsync());
        });
    }

    // Creates a temporary SQLite database using the real employees schema.
    private static async Task WithDatabaseAsync(
        Func<EmployeeRepository, Task> test)
    {
        string databasePath = Path.Combine(
            Path.GetTempPath(),
            $"coffeeloyalty_employee_{Guid.NewGuid():N}.db");

        try
        {
            SqliteConnectionFactory factory = new(databasePath);

            await using (SqliteConnection connection =
                factory.OpenConnection())
            {
                var assembly = typeof(DatabaseInitializer).Assembly;

                await using Stream schemaStream =
                    assembly.GetManifestResourceStream(
                        "CoffeeLoyalty.Core.data.schemas.EmployeesSchema.sql")
                    ?? throw new InvalidOperationException(
                        "Employees schema resource not found.");

                using StreamReader reader = new(schemaStream);
                string schema = await reader.ReadToEndAsync();

                await using SqliteCommand command =
                    connection.CreateCommand();

                command.CommandText = schema;
                await command.ExecuteNonQueryAsync();
            }

            EmployeeRepository repository = new(factory);
            await test(repository);
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
