using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Models;

namespace CoffeeLoyalty.Core.Interfaces.Repositories;

// Defines data access operations for card drink counters (USER_DRINK) with optional transaction support.
public interface IUserDrinkRepository
{
    // Retrieves drink counter records associated with the specified customer across their cards.
    Task<OperationResult<IReadOnlyList<UserDrink>>> GetByCustomerIdAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    // Inserts or increments/updates a drink quantity counter for a specific card, optionally participating in an existing transaction.
    Task<OperationResult<UserDrink>> UpsertQuantityAsync(
        int cardId,
        int drinkId,
        int quantityDelta,
        object? transaction = null,
        CancellationToken cancellationToken = default);
}