
using System.Globalization;
using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.resources;
using Microsoft.Data.Sqlite;

namespace CoffeeLoyalty.Core.Repositories;

// Provides SQLite operations for customer loyalty cards.
public sealed class CardRepository : BaseRepository, ICardRepository
{
    public CardRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    // Returns all cards, including blocked cards.
    public async Task<IReadOnlyList<LoyaltyCard>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT cardID, customerID, isActive
            FROM CARDS
            ORDER BY cardID;
            """;

        await using SqliteConnection connection = OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = sql;

        List<LoyaltyCard> cards = [];

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            cards.Add(MapCard(reader));
        }

        return cards.AsReadOnly();
    }

    // Finds a card by ID or returns null.
    public async Task<LoyaltyCard?> GetByIdAsync(
        int cardId,
        CancellationToken cancellationToken = default)
    {
        if (cardId <= 0)
        {
            return null;
        }

        const string sql = """
            SELECT cardID, customerID, isActive
            FROM CARDS
            WHERE cardID = @cardId;
            """;

        await using SqliteConnection connection = OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.Add("@cardId", SqliteType.Integer).Value = cardId;

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? MapCard(reader)
            : null;
    }

    // Returns every card owned by a customer.
    public async Task<IReadOnlyList<LoyaltyCard>> GetByCustomerIdAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        if (customerId <= 0)
        {
            return Array.Empty<LoyaltyCard>();
        }

        const string sql = """
            SELECT cardID, customerID, isActive
            FROM CARDS
            WHERE customerID = @customerId
            ORDER BY cardID;
            """;

        await using SqliteConnection connection = OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.Add("@customerId", SqliteType.Integer).Value =
            customerId;

        List<LoyaltyCard> cards = [];

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            cards.Add(MapCard(reader));
        }

        return cards.AsReadOnly();
    }

    // Issues a card using a standalone connection.
    public Task<int> AddAsync(
        LoyaltyCard card,
        CancellationToken cancellationToken = default)
    {
        return AddAsync(card, null, cancellationToken);
    }

    // Issues a card inside an optional external transaction.
    public async Task<int> AddAsync(
        LoyaltyCard card,
        object? transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);

        const string sql = """
            INSERT INTO CARDS (customerID, isActive)
            VALUES (@customerId, @isActive);

            SELECT last_insert_rowid();
            """;

        return await WithConnectionAsync(
            transaction,
            async (connection, activeTransaction) =>
            {
                await using SqliteCommand command =
                    connection.CreateCommand();

                command.Transaction = activeTransaction;
                command.CommandText = sql;

                command.Parameters.Add(
                    "@customerId", SqliteType.Integer).Value =
                    card.CustomerId;

                command.Parameters.Add(
                    "@isActive", SqliteType.Integer).Value =
                    card.IsActive ? 1 : 0;

                object? result =
                    await command.ExecuteScalarAsync(cancellationToken);

                int id = Convert.ToInt32(
                    result, CultureInfo.InvariantCulture);

                card.Id = id;
                return id;
            });
    }

    // Changes card status without deleting purchase history.
    public async Task<bool> SetActiveStatusAsync(
        int cardId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        if (cardId <= 0)
        {
            return false;
        }

        const string sql = """
            UPDATE CARDS
            SET isActive = @isActive
            WHERE cardID = @cardId;
            """;

        await using SqliteConnection connection = OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = sql;

        command.Parameters.Add("@cardId", SqliteType.Integer).Value =
            cardId;

        command.Parameters.Add("@isActive", SqliteType.Integer).Value =
            isActive ? 1 : 0;

        int affectedRows =
            await command.ExecuteNonQueryAsync(cancellationToken);

        return affectedRows > 0;
    }

    // Uses the caller's transaction without committing or disposing it.
    private async Task<T> WithConnectionAsync<T>(
        object? transaction,
        Func<SqliteConnection, SqliteTransaction?, Task<T>> action)
    {
        if (transaction is null)
        {
            await using SqliteConnection connection = OpenConnection();
            return await action(connection, null);
        }

        if (transaction is not SqliteTransaction activeTransaction ||
            activeTransaction.Connection is null)
        {
            throw new ArgumentException(
                CoreResources.PurchaseExternalTransactionInvalid,
                nameof(transaction));
        }

        return await action(
            activeTransaction.Connection,
            activeTransaction);
    }

    // Converts a SQLite record into a LoyaltyCard model.
    private static LoyaltyCard MapCard(SqliteDataReader reader)
    {
        return new LoyaltyCard(
            customerId: reader.GetInt32(
                reader.GetOrdinal("customerID")),

            isActive: reader.GetInt32(
                reader.GetOrdinal("isActive")) == 1)
        {
            Id = reader.GetInt32(reader.GetOrdinal("cardID"))
        };
    }
}
