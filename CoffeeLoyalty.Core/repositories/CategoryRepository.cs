
using System.Globalization;
using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Models;
using Microsoft.Data.Sqlite;

namespace CoffeeLoyalty.Core.Repositories;

// Provides SQLite operations for customer loyalty categories.
public sealed class CategoryRepository : BaseRepository, ICategoryRepository
{
    public CategoryRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    // Returns all categories ordered by ID.
    public async Task<IReadOnlyList<CustomerCategory>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT categoryID, categoryName, bonusAccrualRate, upgradeThreshold
            FROM CATEGORIES
            ORDER BY categoryID;
            """;

        await using SqliteConnection connection = OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        List<CustomerCategory> categories = [];

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            categories.Add(MapCategory(reader));
        }

        return categories.AsReadOnly();
    }

    // Returns a category by ID, or null if it does not exist.
    public async Task<CustomerCategory?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return null;
        }

        const string sql = """
            SELECT categoryID, categoryName, bonusAccrualRate, upgradeThreshold
            FROM CATEGORIES
            WHERE categoryID = @id;
            """;

        await using SqliteConnection connection = OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.Add("@id", SqliteType.Integer).Value = id;

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? MapCategory(reader)
            : null;
    }

    // Creates a category and returns its generated ID.
    public async Task<int> AddAsync(
        CustomerCategory category,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);

        const string sql = """
            INSERT INTO CATEGORIES (
                categoryName,
                bonusAccrualRate,
                upgradeThreshold
            )
            VALUES (@name, @rate, @threshold);

            SELECT last_insert_rowid();
            """;

        await using SqliteConnection connection = OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = sql;
        AddCategoryParameters(command, category);

        object? result = await command.ExecuteScalarAsync(cancellationToken);

        int id = Convert.ToInt32(result, CultureInfo.InvariantCulture);
        category.Id = id;

        return id;
    }

    // Updates a category and reports whether it was found.
    public async Task<bool> UpdateAsync(
        CustomerCategory category,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);

        if (category.Id <= 0)
        {
            return false;
        }

        const string sql = """
            UPDATE CATEGORIES
            SET categoryName = @name,
                bonusAccrualRate = @rate,
                upgradeThreshold = @threshold
            WHERE categoryID = @id;
            """;

        await using SqliteConnection connection = OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.Add("@id", SqliteType.Integer).Value = category.Id;
        AddCategoryParameters(command, category);

        int affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);

        return affectedRows > 0;
    }

    // Binds validated model values to SQL parameters.
    private static void AddCategoryParameters(
        SqliteCommand command,
        CustomerCategory category)
    {
        command.Parameters.Add("@name", SqliteType.Text).Value =
            category.CategoryName;

        command.Parameters.Add("@rate", SqliteType.Real).Value =
            (double)category.BonusAccrualRate;

        command.Parameters.Add("@threshold", SqliteType.Integer).Value =
            category.UpgradeThreshold;
    }

    // Converts a database row into a domain model.
    private static CustomerCategory MapCategory(SqliteDataReader reader)
    {
        CustomerCategory category = new(
            categoryName: reader.GetString(
                reader.GetOrdinal("categoryName")),

            bonusAccrualRate: Convert.ToDecimal(
                reader.GetValue(reader.GetOrdinal("bonusAccrualRate")),
                CultureInfo.InvariantCulture),

            upgradeThreshold: reader.GetInt32(
                reader.GetOrdinal("upgradeThreshold")))
        {
            Id = reader.GetInt32(reader.GetOrdinal("categoryID"))
        };

        return category;
    }
}
