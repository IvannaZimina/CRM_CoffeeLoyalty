using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.resources;
using CoffeeLoyalty.Core.Validation;

namespace CoffeeLoyalty.Core.DTOs;

// Represents the structured result of a checkout operation returned by IPurchaseService.
// Extends OperationResult with finalized receipt summary data for UI confirmation views.
public class PurchaseResult : OperationResult
{
    private int _purchaseId;
    private decimal _totalAmount;
    private int _bonusesSpent;
    private int _bonusesEarned;
    private int _updatedBonusBalance;
    private IReadOnlyList<CartItem> _items = Array.Empty<CartItem>();

    // Creates a purchase result instance with optional receipt summary fields.
    public PurchaseResult(
        bool isSuccess,
        string? errorMessage = null,
        string? errorCode = null,
        int purchaseId = 0,
        string? customerName = null,
        decimal totalAmount = 0m,
        int bonusesSpent = 0,
        int bonusesEarned = 0,
        int updatedBonusBalance = 0,
        IEnumerable<CartItem>? items = null)
        : base(isSuccess, errorMessage, errorCode)
    {
        if (isSuccess)
        {
            PurchaseId = purchaseId;
            TotalAmount = totalAmount;
            BonusesSpent = bonusesSpent;
            BonusesEarned = bonusesEarned;
            UpdatedBonusBalance = updatedBonusBalance;
            Items = items is null
                ? Array.Empty<CartItem>()
                : items.ToList().AsReadOnly();
        }
        else
        {
            _purchaseId = purchaseId;
            _totalAmount = totalAmount;
            _bonusesSpent = bonusesSpent;
            _bonusesEarned = bonusesEarned;
            _updatedBonusBalance = updatedBonusBalance;
            _items = items is null
                ? Array.Empty<CartItem>()
                : items.ToList().AsReadOnly();
        }

        CustomerName = string.IsNullOrWhiteSpace(customerName)
            ? null
            : customerName.Trim();
    }

    // Identifier of the persisted purchase record in PURCHASES.
    public int PurchaseId
    {
        get => _purchaseId;
        set
        {
            if (!ValidationHelper.IsPositive(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    string.Format(CoreResources.PurchaseResultValPurchaseIdPositive, nameof(PurchaseId), value));
            }

            _purchaseId = value;
        }
    }

    // Optional customer display name associated with the receipt, or null for walk-in orders.
    public string? CustomerName { get; set; }

    // Final monetary amount of the completed purchase.
    public decimal TotalAmount
    {
        get => _totalAmount;
        set
        {
            if (!ValidationHelper.IsNonNegative(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    string.Format(CoreResources.PurchaseResultValAmountNonNegative, nameof(TotalAmount), value));
            }

            _totalAmount = value;
        }
    }

    // Number of loyalty bonuses spent on the purchase.
    public int BonusesSpent
    {
        get => _bonusesSpent;
        set
        {
            if (!ValidationHelper.IsNonNegative(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    string.Format(CoreResources.PurchaseResultValBonusesNonNegative, nameof(BonusesSpent), value));
            }

            _bonusesSpent = value;
        }
    }

    // Number of loyalty bonuses earned from the purchase.
    public int BonusesEarned
    {
        get => _bonusesEarned;
        set
        {
            if (!ValidationHelper.IsNonNegative(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    string.Format(CoreResources.PurchaseResultValBonusesNonNegative, nameof(BonusesEarned), value));
            }

            _bonusesEarned = value;
        }
    }

    // Updated bonus balance on the customer's card after checkout.
    public int UpdatedBonusBalance
    {
        get => _updatedBonusBalance;
        set
        {
            if (!ValidationHelper.IsNonNegative(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    string.Format(CoreResources.PurchaseResultValBonusesNonNegative, nameof(UpdatedBonusBalance), value));
            }

            _updatedBonusBalance = value;
        }
    }

    // Line items included in the completed purchase receipt.
    public IReadOnlyList<CartItem> Items
    {
        get => _items;
        set
        {
            if (value is null)
            {
                throw new ArgumentNullException(
                    nameof(value),
                    string.Format(CoreResources.PurchaseResultValItemsRequired, nameof(Items)));
            }

            _items = value;
        }
    }

    // Creates a successful PurchaseResult with finalized receipt details.
    public static PurchaseResult Success(
        int purchaseId,
        decimal totalAmount,
        int bonusesSpent,
        int bonusesEarned,
        int updatedBonusBalance,
        IEnumerable<CartItem> items,
        string? customerName = null)
    {
        if (items is null)
        {
            throw new ArgumentNullException(
                nameof(items),
                string.Format(CoreResources.PurchaseResultValItemsRequired, nameof(items)));
        }

        return new PurchaseResult(
            isSuccess: true,
            purchaseId: purchaseId,
            customerName: customerName,
            totalAmount: totalAmount,
            bonusesSpent: bonusesSpent,
            bonusesEarned: bonusesEarned,
            updatedBonusBalance: updatedBonusBalance,
            items: items);
    }

    // Creates a failed PurchaseResult with an error message and optional error code.
    public new static PurchaseResult Failure(
        string errorMessage,
        string? errorCode = null)
    {
        if (!ValidationHelper.HasText(errorMessage))
        {
            throw new ArgumentException(
                string.Format(CoreResources.PurchaseResultValErrorMessageRequired, nameof(errorMessage)),
                nameof(errorMessage));
        }

        return new PurchaseResult(
            isSuccess: false,
            errorMessage: errorMessage.Trim(),
            errorCode: errorCode);
    }
}