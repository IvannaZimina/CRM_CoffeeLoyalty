using CoffeeLoyalty.Core.resources;
using CoffeeLoyalty.Core.Validation;

namespace CoffeeLoyalty.Core.Models;

// Represents an individual drink line item from the PURCHASE_ITEM table.
// Inherits the line-item ID from BaseEntity and preserves unit price at purchase time.
public class PurchaseItem : BaseEntity
{
    private int _purchaseId;
    private int _drinkId;
    private int _quantity;
    private decimal _unitPrice;

    // Creates a receipt line item linking a purchase and a drink with quantity and unit price.
    public PurchaseItem(
        int purchaseId,
        int drinkId,
        int quantity,
        decimal unitPrice)
    {
        PurchaseId = purchaseId;
        DrinkId = drinkId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    // References the parent purchase receipt in PURCHASES.
    // Must be a positive integer identifier.
    public int PurchaseId
    {
        get => _purchaseId;
        set
        {
            if (!ValidationHelper.IsPositive(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    string.Format(CoreResources.PurchaseItemValPurchaseIdPositive, nameof(PurchaseId), value));
            }

            _purchaseId = value;
        }
    }

    // References the ordered drink in DRINKS.
    // Must be a positive integer identifier.
    public int DrinkId
    {
        get => _drinkId;
        set
        {
            if (!ValidationHelper.IsPositive(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    string.Format(CoreResources.PurchaseItemValDrinkIdPositive, nameof(DrinkId), value));
            }

            _drinkId = value;
        }
    }

    // Number of drink units ordered in this receipt line.
    // Matches PURCHASE_ITEM schema constraint requiring quantity > 0.
    public int Quantity
    {
        get => _quantity;
        set
        {
            if (!ValidationHelper.IsPositive(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    string.Format(CoreResources.PurchaseItemValQuantityPositive, nameof(Quantity), value));
            }

            _quantity = value;
        }
    }

    // Price per single drink unit at the time of purchase.
    // Matches PURCHASE_ITEM schema constraint requiring unitPrice >= 0.
    public decimal UnitPrice
    {
        get => _unitPrice;
        set
        {
            if (!ValidationHelper.IsNonNegative(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    string.Format(CoreResources.PurchaseItemValUnitPriceNonNegative, nameof(UnitPrice), value));
            }

            _unitPrice = value;
        }
    }
}