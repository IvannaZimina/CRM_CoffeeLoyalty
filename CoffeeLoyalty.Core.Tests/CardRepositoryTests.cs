
using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.Repositories;
using CoffeeLoyalty.Core.resources;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoffeeLoyalty.Core.Tests;

public class CardRepositoryTests
{
    [Fact]
    public async Task AddAndGetCard_ReturnsSavedValues()
    {
        await WithDatabaseAsync(async (cards, customers, factory, categoryId, customerId) =>
        {
            LoyaltyCard card = new(customerId);

            int id = await cards.AddAsync(card);
            LoyaltyCard? saved = await cards.GetByIdAsync(id);

            Assert.True(id > 0);
            Assert.Equal(id, card.Id);
            Assert.NotNull(saved);
            Assert.Equal(customerId, saved.CustomerId);
            Assert.True(saved.IsActive);
        });
    }

    [Fact]
    public async Task GetByCustomerId_ReturnsMultipleActiveAndBlockedCards()
    {
        await WithDatabaseAsync(async (cards, customers, factory, categoryId, customerId) =>
        {
            await cards.AddAsync(new LoyaltyCard(customerId));
            await cards.AddAsync(new LoyaltyCard(customerId, false));

            IReadOnlyList<LoyaltyCard> owned =
                await cards.GetByCustomerIdAsync(customerId);

            IReadOnlyList<LoyaltyCard> all =
                await cards.GetAllAsync();

            Assert.Equal(2, owned.Count);
            Assert.Equal(2, all.Count);
            Assert.Contains(owned, card => card.IsActive);
            Assert.Contains(owned, card => !card.IsActive);
            Assert.All(owned, card =>
                Assert.Equal(customerId, card.CustomerId));

            Assert.Empty(await cards.GetByCustomerIdAsync(999));
        });
    }

    [Fact]
    public async Task SetActiveStatus_BlocksAndReactivatesCard()
    {
        await WithDatabaseAsync(async (cards, customers, factory, categoryId, customerId) =>
        {
            LoyaltyCard card = new(customerId);
            await cards.AddAsync(card);

            Assert.True(await cards.SetActiveStatusAsync(
                card.Id, false));

            LoyaltyCard? blocked = await cards.GetByIdAsync(card.Id);

            Assert.NotNull(blocked);
            Assert.False(blocked.IsActive);

            Assert.True(await cards.SetActiveStatusAsync(
                card.Id, true));

            LoyaltyCard? reactivated =
                await cards.GetByIdAsync(card.Id);

            Assert.NotNull(reactivated);
            Assert.True(reactivated.IsActive);

            Assert.False(await cards.SetActiveStatusAsync(999, false));
            Assert.False(await cards.SetActiveStatusAsync(0, false));
        });
    }

    [Fact]
    public async Task MissingCard_ReturnsNullOrEmptyCollection()
    {
        await WithDatabaseAsync(async (cards, customers, factory, categoryId, customerId) =>
        {
            Assert.Null(await cards.GetByIdAsync(999));
            Assert.Null(await cards.GetByIdAsync(0));
            Assert.Empty(await cards.GetAllAsync());
            Assert.Empty(await cards.GetByCustomerIdAsync(0));
        });
    }

    [Fact]
    public async Task InvalidCustomerForeignKey_IsRejected()
    {
        await WithDatabaseAsync(async (cards, customers, factory, categoryId, customerId) =>
        {
            SqliteException exception =
                await Assert.ThrowsAsync<SqliteException>(
                    () => cards.AddAsync(new LoyaltyCard(999999)));

            Assert.Equal(19, exception.SqliteErrorCode);
            Assert.Empty(await cards.GetAllAsync());
        });
    }

    [Fact]
    public async Task AddWithTransaction_CommitPersistsCard()
    {
        await WithDatabaseAsync(async (cards, customers, factory, categoryId, customerId) =>
        {
            await using SqliteConnection connection =
                factory.OpenConnection();

            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync();

            LoyaltyCard card = new(customerId);
            int id = await cards.AddAsync(card, transaction);

            await transaction.CommitAsync();

            LoyaltyCard? saved = await cards.GetByIdAsync(id);

            Assert.NotNull(saved);
            Assert.Equal(customerId, saved.CustomerId);
        });
    }

    [Fact]
    public async Task AddWithTransaction_RollbackRemovesCard()
    {
        await WithDatabaseAsync(async (cards, customers, factory, categoryId, customerId) =>
        {
            await using SqliteConnection connection =
                factory.OpenConnection();

            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync();

            int id = await cards.AddAsync(
                new LoyaltyCard(customerId), transaction);

            await transaction.RollbackAsync();

            Assert.Null(await cards.GetByIdAsync(id));
        });
    }

    [Fact]
    public async Task CustomerRegistrationRollback_RemovesCustomerAndCard()
    {
        await WithDatabaseAsync(async (cards, customers, factory, categoryId, customerId) =>
        {
            await using SqliteConnection connection =
                factory.OpenConnection();

            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync();

            Customer customer = new(categoryId, "Temporary Customer");
            int newCustomerId =
                await customers.AddAsync(customer, transaction);

            LoyaltyCard card = new(newCustomerId);
            int cardId = await cards.AddAsync(card, transaction);

            Assert.True(newCustomerId > 0);
            Assert.True(cardId > 0);

            await transaction.RollbackAsync();

            Assert.Null(await customers.GetByIdAsync(newCustomerId));
            Assert.Null(await cards.GetByIdAsync(cardId));
        });
    }

    [Fact]
    public async Task InvalidExternalTransaction_IsRejected()
    {
        await WithDatabaseAsync(async (cards, customers, factory, categoryId, customerId) =>
        {
            LoyaltyCard card = new(customerId);

            ArgumentException exception =
                await Assert.ThrowsAsync<ArgumentException>(
                    () => cards.AddAsync(card, new object()));

            Assert.Contains(
                CoreResources.PurchaseExternalTransactionInvalid,
                exception.Message);

            Assert.Empty(await cards.GetAllAsync());
        });
    }

    // Creates an isolated SQLite database with the required schemas.
    private static async Task WithDatabaseAsync(
        Func<CardRepository, CustomerRepository,
            SqliteConnectionFactory, int, int, Task> test)
    {
        string databasePath = Path.Combine(
            Path.GetTempPath(),
            $"coffeeloyalty_card_{Guid.NewGuid():N}.db");

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

            CustomerRepository customers = new(factory);

            int customerId = await customers.AddAsync(
                new Customer(categoryId, "Test Customer"));

            CardRepository cards = new(factory);

            await test(cards, customers, factory, categoryId, customerId);
        }
        finally
        {
            TestDatabasePool.Clear(databasePath);

            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}
