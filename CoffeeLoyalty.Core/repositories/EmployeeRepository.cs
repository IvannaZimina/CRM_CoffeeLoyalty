
using System.Globalization;
using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.resources;
using Microsoft.Data.Sqlite;

namespace CoffeeLoyalty.Core.Repositories;

// Provides SQLite data access for employee accounts.
public sealed class EmployeeRepository : BaseRepository, IEmployeeRepository
{
    // Creates a repository using the shared connection factory.
    public EmployeeRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    // Returns all employees, including inactive accounts.
    public async Task<IReadOnlyList<Employee>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT employeeID, employeeName, employeeRole,
                   employeePassword, isActive
            FROM EMPLOYEES
            ORDER BY employeeID;
            """;

        await using SqliteConnection connection = OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        List<Employee> employees = [];

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            employees.Add(MapEmployee(reader));
        }

        return employees.AsReadOnly();
    }

    // Finds an employee by ID or returns null.
    public async Task<Employee?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return null;
        }

        const string sql = """
            SELECT employeeID, employeeName, employeeRole,
                   employeePassword, isActive
            FROM EMPLOYEES
            WHERE employeeID = @id;
            """;

        await using SqliteConnection connection = OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.Add("@id", SqliteType.Integer).Value = id;

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? MapEmployee(reader)
            : null;
    }

    // Finds an employee by name or login.
    public async Task<Employee?> GetByNameAsync(
        string employeeName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeName))
        {
            return null;
        }

        const string sql = """
            SELECT employeeID, employeeName, employeeRole,
                   employeePassword, isActive
            FROM EMPLOYEES
            WHERE employeeName = @name;
            """;

        await using SqliteConnection connection = OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.Add("@name", SqliteType.Text).Value =
            employeeName.Trim();

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? MapEmployee(reader)
            : null;
    }

    // Adds an employee and returns the generated ID.
    public async Task<int> AddAsync(
        Employee employee,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employee);

        string passwordHash = RequirePasswordHash(employee);

        const string sql = """
            INSERT INTO EMPLOYEES (
                employeeName,
                employeeRole,
                employeePassword,
                isActive
            )
            VALUES (@name, @role, @password, @isActive);

            SELECT last_insert_rowid();
            """;

        await using SqliteConnection connection = OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = sql;
        AddEmployeeParameters(command, employee, passwordHash);

        object? result = await command.ExecuteScalarAsync(cancellationToken);

        int id = Convert.ToInt32(result, CultureInfo.InvariantCulture);
        employee.Id = id;

        return id;
    }

    // Updates employee details and returns whether a row matched.
    public async Task<bool> UpdateAsync(
        Employee employee,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employee);

        if (employee.Id <= 0)
        {
            return false;
        }

        string passwordHash = RequirePasswordHash(employee);

        const string sql = """
            UPDATE EMPLOYEES
            SET employeeName = @name,
                employeeRole = @role,
                employeePassword = @password,
                isActive = @isActive
            WHERE employeeID = @id;
            """;

        await using SqliteConnection connection = OpenConnection();
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.Add("@id", SqliteType.Integer).Value = employee.Id;

        AddEmployeeParameters(command, employee, passwordHash);

        int affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        return affectedRows > 0;
    }

    // Rejects missing password hashes before SQLite persistence.
    private static string RequirePasswordHash(Employee employee)
    {
        if (string.IsNullOrWhiteSpace(employee.EmployeePassword))
        {
            throw new ArgumentException(
                CoreResources.ValEmployeePasswordHashRequired,
                nameof(employee));
        }

        return employee.EmployeePassword;
    }

    // Adds parameterized values without altering the password hash.
    private static void AddEmployeeParameters(
        SqliteCommand command,
        Employee employee,
        string passwordHash)
    {
        command.Parameters.Add("@name", SqliteType.Text).Value =
            employee.EmployeeName;

        command.Parameters.Add("@role", SqliteType.Text).Value =
            employee.EmployeeRole.ToString();

        command.Parameters.Add("@password", SqliteType.Text).Value =
            passwordHash;

        command.Parameters.Add("@isActive", SqliteType.Integer).Value =
            employee.IsActive ? 1 : 0;
    }

    // Converts a database row into an Employee domain model.
    private static Employee MapEmployee(SqliteDataReader reader)
    {
        Employee employee = new(
            employeeName: reader.GetString(
                reader.GetOrdinal("employeeName")),

            employeeRole: Enum.Parse<UserRole>(
                reader.GetString(reader.GetOrdinal("employeeRole"))),

            employeePassword: reader.GetString(
                reader.GetOrdinal("employeePassword")),

            isActive: reader.GetInt32(
                reader.GetOrdinal("isActive")) == 1)
        {
            Id = reader.GetInt32(reader.GetOrdinal("employeeID"))
        };

        return employee;
    }
}
