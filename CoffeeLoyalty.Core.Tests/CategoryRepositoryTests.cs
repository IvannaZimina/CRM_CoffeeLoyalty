
using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoffeeLoyalty.Core.Tests;

public class CategoryRepositoryTests
{
    [Fact]
    public async Task AddAndGetCategory_ReturnsSavedValues()
    {
        await WithDatabaseAsync(async repository =>
        {
            CustomerCategory category = new("Test Silver", 0m, 50);

            int id = await repository.AddAsync(category);
            CustomerCategory? saved = await repository.GetByIdAsync(id);
            IReadOnlyList<CustomerCategory> all =
                await repository.GetAllAsync();

            Assert.True(id > 0);
            Assert.Equal(id, category.Id);
            Assert.NotNull(saved);
            Assert.Equal("Test Silver", saved.CategoryName);
            Assert.Equal(50, saved.UpgradeThreshold);
            Assert.Equal(0m, saved.BonusAccrualRate);
            Assert.Single(all);
        });
    }

    [Fact]
    public async Task UpdateCategory_PersistsChanges()
    {
        await WithDatabaseAsync(async repository =>
        {
            CustomerCategory category = new("Original", 0m, 0);
            await repository.AddAsync(category);

            category.CategoryName = "Updated";
            category.UpgradeThreshold = 100;
            category.BonusAccrualRate = 2m;

            bool updated = await repository.UpdateAsync(category);
            CustomerCategory? saved =
                await repository.GetByIdAsync(category.Id);

            Assert.True(updated);
            Assert.NotNull(saved);
            Assert.Equal("Updated", saved.CategoryName);
            Assert.Equal(100, saved.UpgradeThreshold);
            Assert.Equal(2m, saved.BonusAccrualRate);
        });
    }

    [Fact]
    public async Task DuplicateCategoryName_IsRejected()
    {
        await WithDatabaseAsync(async repository =>
        {
            await repository.AddAsync(new CustomerCategory("Unique"));

            await Assert.ThrowsAsync<SqliteException>(
                () => repository.AddAsync(
                    new CustomerCategory("Unique")));

            IReadOnlyList<CustomerCategory> all =
                await repository.GetAllAsync();

            Assert.Single(all);
        });
    }

    [Fact]
    public async Task MissingCategory_ReturnsNullOrFalse()
    {
        await WithDatabaseAsync(async repository =>
        {
            Assert.Null(await repository.GetByIdAsync(999));

            CustomerCategory missing = new("Missing")
            {
                Id = 999
            };

            Assert.False(await repository.UpdateAsync(missing));
        });
    }

    // Creates an isolated SQLite database with the real category schema.
    private static async Task WithDatabaseAsync(
        Func<CategoryRepository, Task> test)
    {
        string databasePath = Path.Combine(
            Path.GetTempPath(),
            $"coffeeloyalty_category_{Guid.NewGuid():N}.db");

        try
        {
            SqliteConnectionFactory factory = new(databasePath);

            await using (SqliteConnection connection =
                factory.OpenConnection())
            {
                var assembly = typeof(DatabaseInitializer).Assembly;

                await using Stream schemaStream =
                    assembly.GetManifestResourceStream(
                        "CoffeeLoyalty.Core.data.schemas.CategoriesSchema.sql")
                    ?? throw new InvalidOperationException(
                        "Categories schema resource not found.");

                using StreamReader reader = new(schemaStream);
                string schema = await reader.ReadToEndAsync();

                await using SqliteCommand command =
                    connection.CreateCommand();

                command.CommandText = schema;
                await command.ExecuteNonQueryAsync();
            }

            CategoryRepository repository = new(factory);
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
