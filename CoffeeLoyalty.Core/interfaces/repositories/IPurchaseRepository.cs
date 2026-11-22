using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Models;

namespace CoffeeLoyalty.Core.Interfaces.Repositories;

// Defines data access operations for receipt headers (PURCHASES) with optional transaction support.
public interface IPurchaseRepository
{
    // Saves a new purchase header and returns the persisted Purchase record.
    // Accepts an optional transaction object so checkout operations can remain atomic.
    Task<OperationResult<Purchase>> AddAsync(
        Purchase purchase,
        object? transaction = null,
        CancellationToken cancellationToken = default);

    // Retrieves a single purchase record by its identifier.
    Task<OperationResult<Purchase>> GetByIdAsync(
        int purchaseId,
        CancellationToken cancellationToken = default);

    // Retrieves all purchases associated with a specific customer identifier.
    Task<OperationResult<IReadOnlyList<Purchase>>> GetByCustomerAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    // Retrieves all purchase records across the store history.
    Task<OperationResult<IReadOnlyList<Purchase>>> GetAllAsync(
        CancellationToken cancellationToken = default);
}