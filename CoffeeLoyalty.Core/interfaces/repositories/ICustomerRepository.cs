using CoffeeLoyalty.Core.Models;

namespace CoffeeLoyalty.Core.Interfaces.Repositories;

// Defines the data access operations available for customer records.
// Services can use this interface without knowing how SQLite stores data.
// CustomerRepository will implement these methods in a later task.
public interface ICustomerRepository
{
    // Retrieves all registered customers, including inactive accounts.
    // Returns an empty collection if the database has no customers.
    // IReadOnlyList prevents callers from modifying the returned collection directly.
    Task<IReadOnlyList<Customer>> GetAllAsync(
        CancellationToken cancellationToken = default);

    // Finds a customer using their unique database ID.
    // Returns null when no customer with the requested ID exists.
    Task<Customer?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    // Finds the customer who owns a specific loyalty card.
    // The future repository will use the CARDS table to find its customerID.
    // Returns null if the card does not exist or has no matching customer.
    Task<Customer?> GetByCardAsync(
        int cardId,
        CancellationToken cancellationToken = default);

    // Registers a new customer using the Customer domain model.
    // Returns the database-generated ID of the new customer.
    // The repository will handle database constraints and foreign keys.
    Task<int> AddAsync(
        Customer customer,
        CancellationToken cancellationToken = default);

    // Updates an existing customer's profile and loyalty information.
    // This includes their name, category, bonus balance, and other properties.
    // Returns true if the customer was found and updated.
    Task<bool> UpdateAsync(
        Customer customer,
        CancellationToken cancellationToken = default);

    // Changes the active status of an existing customer account.
    // Setting isActive to false blocks the account without deleting it.
    // Returns false if no customer exists with the specified ID.
    Task<bool> SetActiveStatusAsync(
        int customerId,
        bool isActive,
        CancellationToken cancellationToken = default);
}
