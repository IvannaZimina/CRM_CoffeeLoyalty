using System.Data;
using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.resources;
using Microsoft.Data.Sqlite;

namespace CoffeeLoyalty.Core.Repositories;

// Implements SQLite data access operations for menu drinks (DRINKS).
// Inherits connection and transaction helpers from BaseRepository and implements IDrinkRepository.
public class DrinkRepository : BaseRepository, IDrinkRepository
{
    // Creates a DrinkRepository instance backed by the provided SQLite connection factory.
    public DrinkRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    // Retrieves all drinks from DRINKS, including inactive items.
    public async Task<OperationResult<IReadOnlyList<Drink>>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT drinkID, drinkName, drinkPrice, isActive, drinkType, drinkImageLink
            FROM DRINKS
            ORDER BY drinkID;
            """;

        try
        {
            await using SqliteConnection connection = OpenConnection();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;

            List<Drink> drinks = new();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                drinks.Add(MapDrink(reader));
            }

            return OperationResult<IReadOnlyList<Drink>>.Success(drinks.AsReadOnly());
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<Drink>>.Failure(
                string.Format("{0} {1}", CoreResources.ErrorDatabaseConnection, ex.Message),
                nameof(CoreResources.ErrorDatabaseConnection));
        }
    }

    // Retrieves only active drinks (isActive = 1) from DRINKS.
    public async Task<OperationResult<IReadOnlyList<Drink>>> GetAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT drinkID, drinkName, drinkPrice, isActive, drinkType, drinkImageLink
            FROM DRINKS
            WHERE isActive = 1
            ORDER BY drinkID;
            """;

        try
        {
            await using SqliteConnection connection = OpenConnection();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;

            List<Drink> drinks = new();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                drinks.Add(MapDrink(reader));
            }

            return OperationResult<IReadOnlyList<Drink>>.Success(drinks.AsReadOnly());
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<Drink>>.Failure(
                string.Format("{0} {1}", CoreResources.ErrorDatabaseConnection, ex.Message),
                nameof(CoreResources.ErrorDatabaseConnection));
        }
    }

    // Retrieves a single drink by drinkID.
    public async Task<OperationResult<Drink>> GetByIdAsync(
        int drinkId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT drinkID, drinkName, drinkPrice, isActive, drinkType, drinkImageLink
            FROM DRINKS
            WHERE drinkID = @drinkId;
            """;

        try
        {
            await using SqliteConnection connection = OpenConnection();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add("@drinkId", SqliteType.Integer).Value = drinkId;

            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return OperationResult<Drink>.Failure(
                    string.Format(CoreResources.DrinkValNameRequired, nameof(drinkId)),
                    nameof(GetByIdAsync));
            }

            Drink drink = MapDrink(reader);
            return OperationResult<Drink>.Success(drink);
        }
        catch (Exception ex)
        {
            return OperationResult<Drink>.Failure(
                string.Format("{0} {1}", CoreResources.ErrorDatabaseConnection, ex.Message),
                nameof(CoreResources.ErrorDatabaseConnection));
        }
    }

    // Inserts a new drink into DRINKS and returns the created record with its assigned drinkID.
    public async Task<OperationResult<Drink>> AddAsync(
        Drink drink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drink);

        const string sql = """
            INSERT INTO DRINKS (drinkName, drinkPrice, isActive, drinkType, drinkImageLink)
            VALUES (@drinkName, @drinkPrice, @isActive, @drinkType, @drinkImageLink);
            SELECT last_insert_rowid();
            """;

        try
        {
            await using SqliteConnection connection = OpenConnection();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;

            command.Parameters.Add("@drinkName", SqliteType.Text).Value = drink.DrinkName;
            command.Parameters.Add("@drinkPrice", SqliteType.Real).Value = Convert.ToDouble(drink.DrinkPrice);
            command.Parameters.Add("@isActive", SqliteType.Integer).Value = drink.IsActive ? 1 : 0;
            command.Parameters.Add("@drinkType", SqliteType.Text).Value = drink.DrinkType;
            command.Parameters.Add("@drinkImageLink", SqliteType.Text).Value =
                (object?)drink.DrinkImageLink ?? DBNull.Value;

            object? scalar = await command.ExecuteScalarAsync(cancellationToken);
            int newId = Convert.ToInt32(scalar);

            Drink created = new(
                drink.DrinkName,
                drink.DrinkPrice,
                drink.DrinkType,
                drink.IsActive,
                drink.DrinkImageLink)
            {
                Id = newId
            };

            return OperationResult<Drink>.Success(created);
        }
        catch (Exception ex)
        {
            return OperationResult<Drink>.Failure(
                string.Format("{0} {1}", CoreResources.ErrorDatabaseConnection, ex.Message),
                nameof(CoreResources.ErrorDatabaseConnection));
        }
    }

    // Updates an existing drink record in DRINKS by drinkID.
    public async Task<OperationResult> UpdateAsync(
        Drink drink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drink);

        const string sql = """
            UPDATE DRINKS
            SET drinkName = @drinkName,
                drinkPrice = @drinkPrice,
                isActive = @isActive,
                drinkType = @drinkType,
                drinkImageLink = @drinkImageLink
            WHERE drinkID = @drinkId;
            """;

        try
        {
            await using SqliteConnection connection = OpenConnection();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;

            command.Parameters.Add("@drinkId", SqliteType.Integer).Value = drink.Id;
            command.Parameters.Add("@drinkName", SqliteType.Text).Value = drink.DrinkName;
            command.Parameters.Add("@drinkPrice", SqliteType.Real).Value = Convert.ToDouble(drink.DrinkPrice);
            command.Parameters.Add("@isActive", SqliteType.Integer).Value = drink.IsActive ? 1 : 0;
            command.Parameters.Add("@drinkType", SqliteType.Text).Value = drink.DrinkType;
            command.Parameters.Add("@drinkImageLink", SqliteType.Text).Value =
                (object?)drink.DrinkImageLink ?? DBNull.Value;

            int rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (rowsAffected == 0)
            {
                return OperationResult.Failure(
                    string.Format(CoreResources.DrinkValNameRequired, nameof(drink.Id)),
                    nameof(UpdateAsync));
            }

            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(
                string.Format("{0} {1}", CoreResources.ErrorDatabaseConnection, ex.Message),
                nameof(CoreResources.ErrorDatabaseConnection));
        }
    }

    // Updates the isActive flag of a drink in DRINKS by drinkID.
    public async Task<OperationResult> SetActiveStatusAsync(
        int drinkId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE DRINKS
            SET isActive = @isActive
            WHERE drinkID = @drinkId;
            """;

        try
        {
            await using SqliteConnection connection = OpenConnection();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;

            command.Parameters.Add("@drinkId", SqliteType.Integer).Value = drinkId;
            command.Parameters.Add("@isActive", SqliteType.Integer).Value = isActive ? 1 : 0;

            int rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (rowsAffected == 0)
            {
                return OperationResult.Failure(
                    string.Format(CoreResources.DrinkValNameRequired, nameof(drinkId)),
                    nameof(SetActiveStatusAsync));
            }

            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(
                string.Format("{0} {1}", CoreResources.ErrorDatabaseConnection, ex.Message),
                nameof(CoreResources.ErrorDatabaseConnection));
        }
    }

    // Safely maps a DRINKS row to a Drink model, handling nullable drinkImageLink values.
    private static Drink MapDrink(SqliteDataReader reader)
    {
        int idOrdinal = reader.GetOrdinal("drinkID");
        int nameOrdinal = reader.GetOrdinal("drinkName");
        int priceOrdinal = reader.GetOrdinal("drinkPrice");
        int activeOrdinal = reader.GetOrdinal("isActive");
        int typeOrdinal = reader.GetOrdinal("drinkType");
        int imageOrdinal = reader.GetOrdinal("drinkImageLink");

        string drinkName = reader.GetString(nameOrdinal);
        decimal drinkPrice = Convert.ToDecimal(reader.GetDouble(priceOrdinal));
        bool isActive = reader.GetInt32(activeOrdinal) != 0;
        string drinkType = reader.GetString(typeOrdinal);
        string? drinkImageLink = reader.IsDBNull(imageOrdinal)
            ? null
            : reader.GetString(imageOrdinal);

        return new Drink(drinkName, drinkPrice, drinkType, isActive, drinkImageLink)
        {
            Id = reader.GetInt32(idOrdinal)
        };
    }
}