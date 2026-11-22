using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Models;

namespace CoffeeLoyalty.Core.Interfaces.Repositories;

// Defines data access operations for receipt line items (PURCHASE_ITEM) with optional transaction support.
public interface IPurchaseItemRepository
{
    // Saves a single receipt line item, optionally participating in an existing transaction.
    Task<OperationResult<PurchaseItem>> AddAsync(
        PurchaseItem item,
        object? transaction = null,
        CancellationToken cancellationToken = default);

    // Saves multiple receipt line items together, optionally participating in an existing transaction.
    Task<OperationResult> AddRangeAsync(
        IEnumerable<PurchaseItem> items,
        object? transaction = null,
        CancellationToken cancellationToken = default);

    // Retrieves all line items belonging to a specific purchase receipt.
    Task<OperationResult<IReadOnlyList<PurchaseItem>>> GetByPurchaseIdAsync(
        int purchaseId,
        CancellationToken cancellationToken = default);
}