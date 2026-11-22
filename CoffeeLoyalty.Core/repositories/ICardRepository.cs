
using CoffeeLoyalty.Core.Models;

namespace CoffeeLoyalty.Core.Interfaces.Repositories;

// Defines data access operations for customer loyalty cards.
// Services use this interface without depending on SQLite directly.
// CardRepository will implement the actual database queries later.
public interface ICardRepository
{
    // Finds a loyalty card using its unique database ID.
    // Returns null if the requested card does not exist.
    Task<LoyaltyCard?> GetByIdAsync(
        int cardId,
        CancellationToken cancellationToken = default);

    // Retrieves all loyalty cards belonging to a specific customer.
    // A customer may own multiple active or blocked cards.
    // Returns an empty collection if the customer has no cards.
    Task<IReadOnlyList<LoyaltyCard>> GetByCustomerIdAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    // Issues a new loyalty card linked to an existing customer.
    // Returns the database-generated ID of the new card.
    // The repository will verify foreign key and database constraints.
    Task<int> AddAsync(
        LoyaltyCard card,
        CancellationToken cancellationToken = default);

    // Activates or blocks an existing loyalty card.
    // Setting isActive to false preserves the card and purchase history.
    // Returns false when no card exists with the specified ID.
    Task<bool> SetActiveStatusAsync(
        int cardId,
        bool isActive,
        CancellationToken cancellationToken = default);
}
