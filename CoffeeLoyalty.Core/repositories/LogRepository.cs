
using System.Globalization;
using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Models;
using Microsoft.Data.Sqlite;

namespace CoffeeLoyalty.Core.Repositories;

// Stores and retrieves audit and error records in SQLite.
public sealed class LogRepository : BaseRepository, ILogRepository
{
    public LogRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    // Saves an event and returns its generated database ID.
    public async Task<int> AddAsync(
        LogEntry logEntry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logEntry);

        const string sql = """
            INSERT INTO LOGS (
                objectID, objectType, actionType,
                description, timeStamp
            )
            VALUES (
                @objectId, @objectType, @actionType,
                @description, @timeStamp
            );

            SELECT last_insert_rowid();
            """;

        await using SqliteConnection connection = OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = sql;

        command.Parameters.Add("@objectId", SqliteType.Integer).Value =
            (object?)logEntry.ObjectId ?? DBNull.Value;

        command.Parameters.Add("@objectType", SqliteType.Text).Value =
            logEntry.ObjectType;

        command.Parameters.Add("@actionType", SqliteType.Text).Value =
            logEntry.ActionType;

        command.Parameters.Add("@description", SqliteType.Text).Value =
            logEntry.Description;

        command.Parameters.Add("@timeStamp", SqliteType.Text).Value =
            logEntry.TimeStamp.ToString("O", CultureInfo.InvariantCulture);

        object? result =
            await command.ExecuteScalarAsync(cancellationToken);

        int id = Convert.ToInt32(result, CultureInfo.InvariantCulture);
        logEntry.Id = id;

        return id;
    }

    // Returns every log, newest first.
    public async Task<IReadOnlyList<LogEntry>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT logID, objectID, objectType,
                   actionType, description, timeStamp
            FROM LOGS
            ORDER BY julianday(timeStamp) DESC, logID DESC;
            """;

        await using SqliteConnection connection = OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = sql;

        return await ReadLogsAsync(command, cancellationToken);
    }

    // Retrieves events within an inclusive date and time range.
    public async Task<IReadOnlyList<LogEntry>> GetByDateRangeAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        if (startDate > endDate)
        {
            throw new ArgumentOutOfRangeException(nameof(endDate));
        }

        const string sql = """
            SELECT logID, objectID, objectType,
                   actionType, description, timeStamp
            FROM LOGS
            WHERE julianday(timeStamp) >= julianday(@startDate)
              AND julianday(timeStamp) <= julianday(@endDate)
            ORDER BY julianday(timeStamp) DESC, logID DESC;
            """;

        await using SqliteConnection connection = OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = sql;

        command.Parameters.Add("@startDate", SqliteType.Text).Value =
            startDate.ToString("O", CultureInfo.InvariantCulture);

        command.Parameters.Add("@endDate", SqliteType.Text).Value =
            endDate.ToString("O", CultureInfo.InvariantCulture);

        return await ReadLogsAsync(command, cancellationToken);
    }

    // Retrieves events with the specified action type.
    public async Task<IReadOnlyList<LogEntry>> GetByActionTypeAsync(
        string actionType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actionType))
        {
            return Array.Empty<LogEntry>();
        }

        const string sql = """
            SELECT logID, objectID, objectType,
                   actionType, description, timeStamp
            FROM LOGS
            WHERE actionType = @actionType
            ORDER BY julianday(timeStamp) DESC, logID DESC;
            """;

        await using SqliteConnection connection = OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.Add("@actionType", SqliteType.Text).Value =
            actionType.Trim();

        return await ReadLogsAsync(command, cancellationToken);
    }

    // Reads query results into domain models.
    private static async Task<IReadOnlyList<LogEntry>> ReadLogsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        List<LogEntry> logs = [];

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            logs.Add(MapLog(reader));
        }

        return logs.AsReadOnly();
    }

    // Converts a database row into a LogEntry.
    private static LogEntry MapLog(SqliteDataReader reader)
    {
        int objectIdIndex = reader.GetOrdinal("objectID");

        int? objectId = reader.IsDBNull(objectIdIndex)
            ? null
            : reader.GetInt32(objectIdIndex);

        DateTime timeStamp = DateTime.Parse(
            reader.GetString(reader.GetOrdinal("timeStamp")),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

        return new LogEntry(
            objectType: reader.GetString(
                reader.GetOrdinal("objectType")),
            actionType: reader.GetString(
                reader.GetOrdinal("actionType")),
            description: reader.GetString(
                reader.GetOrdinal("description")),
            objectId: objectId,
            timeStamp: timeStamp)
        {
            Id = reader.GetInt32(reader.GetOrdinal("logID"))
        };
    }
}
