using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.resources;
using CoffeeLoyalty.Core.Validation;

namespace CoffeeLoyalty.Core.DTOs;

// Represents an in-memory checkout request passed to IPurchaseService.
// Does not inherit from BaseEntity and decouples checkout inputs from UI layers.
public class PurchaseRequest
{
    private int? _cardId;
    private int _employeeId;
    private IReadOnlyList<CartItem> _items = Array.Empty<CartItem>();
    private int _bonusesToSpend;

    // Creates a checkout request with required employee and cart items.
    // CardId is optional so walk-in purchases without a loyalty card are supported.
    public PurchaseRequest(
        int employeeId,
        IEnumerable<CartItem> items,
        int bonusesToSpend = 0,
        int? cardId = null)
    {
        CardId = cardId;
        EmployeeId = employeeId;
        Items = items is null
            ? throw new ArgumentNullException(
                nameof(items),
                string.Format(CoreResources.PurchaseRequestValItemsRequired, nameof(Items)))
            : items.ToList().AsReadOnly();
        BonusesToSpend = bonusesToSpend;
    }

    // Optional reference to the loyalty card used in the checkout.
    // Must be a positive ID when provided; null represents a walk-in purchase.
    public int? CardId
    {
        get => _cardId;
        set
        {
            if (value.HasValue && !ValidationHelper.IsPositive(value.Value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    string.Format(CoreResources.PurchaseRequestValCardIdPositive, nameof(CardId), value.Value));
            }

            _cardId = value;
        }
    }

    // References the employee processing the checkout.
    // Must be a positive integer identifier.
    public int EmployeeId
    {
        get => _employeeId;
        set
        {
            if (!ValidationHelper.IsPositive(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    string.Format(CoreResources.PurchaseRequestValEmployeeIdPositive, nameof(EmployeeId), value));
            }

            _employeeId = value;
        }
    }

    // Collection of cart line items submitted for checkout.
    // Cannot be null.
    public IReadOnlyList<CartItem> Items
    {
        get => _items;
        set
        {
            if (value is null)
            {
                throw new ArgumentNullException(
                    nameof(value),
                    string.Format(CoreResources.PurchaseRequestValItemsRequired, nameof(Items)));
            }

            _items = value;
        }
    }

    // Number of loyalty bonuses the customer wants to redeem on this purchase.
    // Must be zero or greater.
    public int BonusesToSpend
    {
        get => _bonusesToSpend;
        set
        {
            if (!ValidationHelper.IsNonNegative(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    string.Format(CoreResources.PurchaseRequestValBonusesToSpendNonNegative, nameof(BonusesToSpend), value));
            }

            _bonusesToSpend = value;
        }
    }
}