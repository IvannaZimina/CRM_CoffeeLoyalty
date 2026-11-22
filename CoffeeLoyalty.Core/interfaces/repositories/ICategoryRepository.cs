using CoffeeLoyalty.Core.Models;

namespace CoffeeLoyalty.Core.Interfaces.Repositories;

// Defines the operations available for customer loyalty categories.
// Services use this interface without depending directly on SQLite.
// CategoryRepository will provide the actual database implementation.
public interface ICategoryRepository
{
    // Retrieves all loyalty categories, such as Basic, Silver, and Gold.
    // Returns an empty collection when no categories are available.
    // IReadOnlyList prevents changes to the collection through this interface.
    Task<IReadOnlyList<CustomerCategory>> GetAllAsync(
        CancellationToken cancellationToken = default);

    // Finds a single category using its database ID.
    // Returns null if the requested category does not exist.
    Task<CustomerCategory?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    // Adds a new loyalty category to the database.
    // Returns the ID generated for the newly created category.
    // The repository will handle duplicate names and database constraints.
    Task<int> AddAsync(
        CustomerCategory category,
        CancellationToken cancellationToken = default);

    // Updates an existing category using its Id.
    // Returns true if a record was updated, or false if no record matched.
    Task<bool> UpdateAsync(
        CustomerCategory category,
        CancellationToken cancellationToken = default);
}
