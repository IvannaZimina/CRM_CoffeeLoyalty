
using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.Repositories;
using CoffeeLoyalty.Core.resources;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoffeeLoyalty.Core.Tests;

public class CustomerRepositoryTests
{
    [Fact]
    public async Task AddAndReadCustomer_RoundTripsFields()
    {
        await WithDatabaseAsync(async (repository, factory, categoryId) =>
        {
            DateTime registered = new(
                2026, 11, 23, 12, 30, 0, DateTimeKind.Utc);

            Customer customer = new(
                categoryId,
                " O'Brien ",
                bonusBalance: 25,
                birthDate: new DateOnly(1996, 5, 20),
                timeStamp: registered);

            int id = await repository.AddAsync(customer);
            Customer? saved = await repository.GetByIdAsync(id);
            IReadOnlyList<Customer> all = await repository.GetAllAsync();

            Assert.True(id > 0);
            Assert.Equal(id, customer.Id);
            Assert.NotNull(saved);
            Assert.Equal("O'Brien", saved.CustomerName);
            Assert.Equal(categoryId, saved.CategoryId);
            Assert.Equal(25, saved.BonusBalance);
            Assert.Equal(new DateOnly(1996, 5, 20), saved.BirthDate);
            Assert.Equal(registered, saved.TimeStamp);
            Assert.True(saved.IsActive);
            Assert.Single(all);
        });
    }

    [Fact]
    public async Task GetByCard_ReturnsOwnerForInactiveCard()
    {
        await WithDatabaseAsync(async (repository, factory, categoryId) =>
        {
            Customer customer = new(categoryId, "Card Owner");
            await repository.AddAsync(customer);

            await using SqliteConnection connection =
                factory.OpenConnection();

            await using SqliteCommand command = connection.CreateCommand();

            command.CommandText = """
                INSERT INTO CARDS (customerID, isActive)
                VALUES (@customerId, 0);
                SELECT last_insert_rowid();
                """;

            command.Parameters.Add("@customerId", SqliteType.Integer).Value =
                customer.Id;

            int cardId = Convert.ToInt32(
                await command.ExecuteScalarAsync());

            Customer? owner = await repository.GetByCardAsync(cardId);

            Assert.NotNull(owner);
            Assert.Equal(customer.Id, owner.Id);
            Assert.Null(await repository.GetByCardAsync(999));
        });
    }

    [Fact]
    public async Task UpdateCustomer_PersistsProfileAndBonusChanges()
    {
        await WithDatabaseAsync(async (repository, factory, categoryId) =>
        {
            Customer customer = new(categoryId, "Original", 10);
            await repository.AddAsync(customer);

            customer.CustomerName = "Updated";
            customer.BonusBalance = 75;
            customer.BirthDate = new DateOnly(2000, 1, 15);
            customer.IsActive = false;

            bool updated = await repository.UpdateAsync(customer);
            Customer? saved = await repository.GetByIdAsync(customer.Id);

            Assert.True(updated);
            Assert.NotNull(saved);
            Assert.Equal("Updated", saved.CustomerName);
            Assert.Equal(75, saved.BonusBalance);
            Assert.Equal(new DateOnly(2000, 1, 15), saved.BirthDate);
            Assert.False(saved.IsActive);
        });
    }

    [Fact]
    public async Task SetActiveStatus_UpdatesOnlyExistingCustomer()
    {
        await WithDatabaseAsync(async (repository, factory, categoryId) =>
        {
            Customer customer = new(categoryId, "Active Customer");
            await repository.AddAsync(customer);

            Assert.True(await repository.SetActiveStatusAsync(
                customer.Id, false));

            Customer? saved = await repository.GetByIdAsync(customer.Id);

            Assert.NotNull(saved);
            Assert.False(saved.IsActive);

            Assert.False(await repository.SetActiveStatusAsync(
                999, false));

            Assert.Null(await repository.GetByIdAsync(999));
        });
    }

    [Fact]
    public async Task InvalidCategoryForeignKey_IsRejected()
    {
        await WithDatabaseAsync(async (repository, factory, categoryId) =>
        {
            Customer customer = new(999999, "Invalid Category");

            SqliteException exception =
                await Assert.ThrowsAsync<SqliteException>(
                    () => repository.AddAsync(customer));

            Assert.Equal(19, exception.SqliteErrorCode);
            Assert.Empty(await repository.GetAllAsync());
        });
    }

    [Fact]
    public async Task UpdateWithTransaction_RollbackRestoresBonusBalance()
    {
        await WithDatabaseAsync(async (repository, factory, categoryId) =>
        {
            Customer customer = new(categoryId, "Rollback Customer", 12);
            await repository.AddAsync(customer);

            await using SqliteConnection connection =
                factory.OpenConnection();

            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync();

            customer.BonusBalance = 82;

            Assert.True(await repository.UpdateAsync(
                customer, transaction));

            await transaction.RollbackAsync();

            Customer? saved = await repository.GetByIdAsync(customer.Id);

            Assert.NotNull(saved);
            Assert.Equal(12, saved.BonusBalance);
        });
    }

    [Fact]
    public async Task UpdateWithTransaction_CommitPersistsBonusBalance()
    {
        await WithDatabaseAsync(async (repository, factory, categoryId) =>
        {
            Customer customer = new(categoryId, "Commit Customer", 20);
            await repository.AddAsync(customer);

            await using SqliteConnection connection =
                factory.OpenConnection();

            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync();

            customer.BonusBalance = 95;

            Assert.True(await repository.UpdateAsync(
                customer, transaction));

            await transaction.CommitAsync();

            Customer? saved = await repository.GetByIdAsync(customer.Id);

            Assert.NotNull(saved);
            Assert.Equal(95, saved.BonusBalance);
        });
    }

    [Fact]
    public async Task AddWithTransaction_RollbackRemovesCustomer()
    {
        await WithDatabaseAsync(async (repository, factory, categoryId) =>
        {
            await using SqliteConnection connection =
                factory.OpenConnection();

            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync();

            Customer customer = new(categoryId, "Temporary Customer");

            int id = await repository.AddAsync(customer, transaction);

            Assert.True(id > 0);

            await transaction.RollbackAsync();

            Assert.Null(await repository.GetByIdAsync(id));
        });
    }

    [Fact]
    public async Task InvalidExternalTransaction_IsRejected()
    {
        await WithDatabaseAsync(async (repository, factory, categoryId) =>
        {
            Customer customer = new(categoryId, "Invalid Transaction");
            await repository.AddAsync(customer);

            ArgumentException exception =
                await Assert.ThrowsAsync<ArgumentException>(
                    () => repository.UpdateAsync(customer, new object()));

            Assert.Contains(
                CoreResources.PurchaseExternalTransactionInvalid,
                exception.Message);
        });
    }

    // Creates an isolated database using the real category, customer and card schemas.
    private static async Task WithDatabaseAsync(
        Func<CustomerRepository, SqliteConnectionFactory, int, Task> test)
    {
        string databasePath = Path.Combine(
            Path.GetTempPath(),
            $"coffeeloyalty_customer_{Guid.NewGuid():N}.db");

        try
        {
            SqliteConnectionFactory factory = new(databasePath);
            int categoryId;

            await using (SqliteConnection connection =
                factory.OpenConnection())
            {
                string[] schemas =
                [
                    "CategoriesSchema.sql",
                    "CustomersSchema.sql",
                    "CardsSchema.sql"
                ];

                foreach (string schemaName in schemas)
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

                await using SqliteCommand seedCommand =
                    connection.CreateCommand();

                seedCommand.CommandText = """
                    INSERT INTO CATEGORIES (
                        categoryName, bonusAccrualRate, upgradeThreshold
                    )
                    VALUES ('Test', 0, 0);
                    SELECT last_insert_rowid();
                    """;

                categoryId = Convert.ToInt32(
                    await seedCommand.ExecuteScalarAsync());
            }

            CustomerRepository repository = new(factory);
            await test(repository, factory, categoryId);
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
