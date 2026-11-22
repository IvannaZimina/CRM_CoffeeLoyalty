
using CoffeeLoyalty.Core.Models;

namespace CoffeeLoyalty.Core.Interfaces.Repositories;

// Defines asynchronous data access operations for employee accounts.
public interface IEmployeeRepository
{
    // Retrieves all employees, including inactive accounts.
    Task<IReadOnlyList<Employee>> GetAllAsync(
        CancellationToken cancellationToken = default);

    // Finds an employee by ID, or returns null if not found.
    Task<Employee?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    // Finds an employee by name or login, useful for authentication.
    Task<Employee?> GetByNameAsync(
        string employeeName,
        CancellationToken cancellationToken = default);

    // Adds an employee and returns the generated database ID.
    Task<int> AddAsync(
        Employee employee,
        CancellationToken cancellationToken = default);

    // Updates employee details, role, credentials, or active status.
    Task<bool> UpdateAsync(
        Employee employee,
        CancellationToken cancellationToken = default);
}
