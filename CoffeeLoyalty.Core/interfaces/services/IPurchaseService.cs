using CoffeeLoyalty.Core.DTOs;

namespace CoffeeLoyalty.Core.Interfaces.Services;

// Defines the end-to-end checkout workflow coordinating validation, bonus calculation,
// atomic receipt persistence, card balance updates, drink counters, and audit logging.
public interface IPurchaseService
{
    // Validates and processes a checkout request atomically, returning a structured PurchaseResult.
    Task<PurchaseResult> ProcessPurchaseAsync(
        PurchaseRequest request,
        CancellationToken cancellationToken = default);
}