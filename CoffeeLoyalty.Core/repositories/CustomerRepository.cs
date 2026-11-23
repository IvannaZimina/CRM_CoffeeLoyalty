
using System.Globalization;
using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.resources;
using Microsoft.Data.Sqlite;

namespace CoffeeLoyalty.Core.Repositories;

// Provides SQLite access to customer accounts and loyalty balances.
public sealed class CustomerRepository : BaseRepository, ICustomerRepository
{
    public CustomerRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    // Returns all customers, including inactive accounts.
    public async Task<IReadOnlyList<Customer>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT customerID, categoryID, customerName, bonusBalance,
                   birthDate, isActive, timeStamp
            FROM CUSTOMERS
            ORDER BY customerID;
            """;

        await using SqliteConnection connection = OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        List<Customer> customers = [];

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            customers.Add(MapCustomer(reader));
        }

        return customers.AsReadOnly();
    }

    // Finds a customer by ID or returns null.
    public async Task<Customer?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return null;
        }

        const string sql = """
            SELECT customerID, categoryID, customerName, bonusBalance,
                   birthDate, isActive, timeStamp
            FROM CUSTOMERS
            WHERE customerID = @id;
            """;

        await using SqliteConnection connection = OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.Add("@id", SqliteType.Integer).Value = id;

        return await ReadCustomerAsync(command, cancellationToken);
    }

    // Finds the owner of a card, regardless of the card's active status.
    public async Task<Customer?> GetByCardAsync(
        int cardId,
        CancellationToken cancellationToken = default)
    {
        if (cardId <= 0)
        {
            return null;
        }

        const string sql = """
            SELECT c.customerID, c.categoryID, c.customerName,
                   c.bonusBalance, c.birthDate, c.isActive, c.timeStamp
            FROM CUSTOMERS c
            INNER JOIN CARDS card ON card.customerID = c.customerID
            WHERE card.cardID = @cardId;
            """;

        await using SqliteConnection connection = OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.Add("@cardId", SqliteType.Integer).Value = cardId;

        return await ReadCustomerAsync(command, cancellationToken);
    }

    // Adds a customer using a standalone database connection.
    public Task<int> AddAsync(
        Customer customer,
        CancellationToken cancellationToken = default)
    {
        return AddAsync(customer, null, cancellationToken);
    }

    // Adds a customer using an optional external transaction.
    public async Task<int> AddAsync(
        Customer customer,
        object? transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        const string sql = """
            INSERT INTO CUSTOMERS (
                categoryID, customerName, bonusBalance,
                birthDate, isActive, timeStamp
            )
            VALUES (
                @categoryId, @name, @balance,
                @birthDate, @isActive, @timeStamp
            );

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
                AddCustomerParameters(command, customer);

                object? result =
                    await command.ExecuteScalarAsync(cancellationToken);

                int id = Convert.ToInt32(
                    result, CultureInfo.InvariantCulture);

                customer.Id = id;
                return id;
            });
    }

    // Updates a customer using a standalone connection.
    public Task<bool> UpdateAsync(
        Customer customer,
        CancellationToken cancellationToken = default)
    {
        return UpdateAsync(customer, null, cancellationToken);
    }

    // Updates customer details within an optional external transaction.
    public async Task<bool> UpdateAsync(
        Customer customer,
        object? transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        if (customer.Id <= 0)
        {
            return false;
        }

        const string sql = """
            UPDATE CUSTOMERS
            SET categoryID = @categoryId,
                customerName = @name,
                bonusBalance = @balance,
                birthDate = @birthDate,
                isActive = @isActive,
                timeStamp = @timeStamp
            WHERE customerID = @id;
            """;

        return await WithConnectionAsync(
            transaction,
            async (connection, activeTransaction) =>
            {
                await using SqliteCommand command =
                    connection.CreateCommand();

                command.Transaction = activeTransaction;
                command.CommandText = sql;

                command.Parameters.Add("@id", SqliteType.Integer).Value =
                    customer.Id;

                AddCustomerParameters(command, customer);

                int affectedRows =
                    await command.ExecuteNonQueryAsync(cancellationToken);

                return affectedRows > 0;
            });
    }

    // Activates or deactivates a customer without deleting history.
    public async Task<bool> SetActiveStatusAsync(
        int customerId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        if (customerId <= 0)
        {
            return false;
        }

        const string sql = """
            UPDATE CUSTOMERS
            SET isActive = @isActive
            WHERE customerID = @id;
            """;

        await using SqliteConnection connection = OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.Add("@id", SqliteType.Integer).Value =
            customerId;

        command.Parameters.Add("@isActive", SqliteType.Integer).Value =
            isActive ? 1 : 0;

        int affectedRows =
            await command.ExecuteNonQueryAsync(cancellationToken);

        return affectedRows > 0;
    }

    // Reuses the caller's transaction without committing or disposing it.
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

        return await action(activeTransaction.Connection, activeTransaction);
    }

    // Executes a customer lookup and maps the first matching record.
    private static async Task<Customer?> ReadCustomerAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? MapCustomer(reader)
            : null;
    }

    // Binds model fields to SQL parameters.
    private static void AddCustomerParameters(
        SqliteCommand command,
        Customer customer)
    {
        command.Parameters.Add("@categoryId", SqliteType.Integer).Value =
            customer.CategoryId;

        command.Parameters.Add("@name", SqliteType.Text).Value =
            customer.CustomerName;

        command.Parameters.Add("@balance", SqliteType.Integer).Value =
            customer.BonusBalance;

        command.Parameters.Add("@birthDate", SqliteType.Text).Value =
            customer.BirthDate is DateOnly date
                ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : (object)DBNull.Value;

        command.Parameters.Add("@isActive", SqliteType.Integer).Value =
            customer.IsActive ? 1 : 0;

        command.Parameters.Add("@timeStamp", SqliteType.Text).Value =
            customer.TimeStamp.ToString("O", CultureInfo.InvariantCulture);
    }

    // Converts a SQLite row into a Customer domain model.
    private static Customer MapCustomer(SqliteDataReader reader)
    {
        int birthDateIndex = reader.GetOrdinal("birthDate");

        DateOnly? birthDate = reader.IsDBNull(birthDateIndex)
            ? null
            : DateOnly.ParseExact(
                reader.GetString(birthDateIndex),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);

        DateTime timeStamp = DateTime.Parse(
            reader.GetString(reader.GetOrdinal("timeStamp")),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

        return new Customer(
            categoryId: reader.GetInt32(
                reader.GetOrdinal("categoryID")),
            customerName: reader.GetString(
                reader.GetOrdinal("customerName")),
            bonusBalance: reader.GetInt32(
                reader.GetOrdinal("bonusBalance")),
            birthDate: birthDate,
            isActive: reader.GetInt32(
                reader.GetOrdinal("isActive")) == 1,
            timeStamp: timeStamp)
        {
            Id = reader.GetInt32(reader.GetOrdinal("customerID"))
        };
    }
}
