using CoffeeLoyalty.Core.resources;
using CoffeeLoyalty.Core.Validation;

namespace CoffeeLoyalty.Core.Models;

// Represents a completed purchase receipt header from the PURCHASES table.
// Inherits the receipt ID from BaseEntity and validates totals, bonuses, and timestamp.
public class Purchase : BaseEntity
{
    private int? _cardId;
    private int _employeeId;
    private decimal _totalAmount;
    private int _bonusesSpent;
    private int _bonusesEarned;
    private DateTime _timeStamp;

    // Creates a purchase record with required employee, total amount, and bonuses.
    // CardId is optional so walk-in purchases without a loyalty card are supported.
    public Purchase(
        int employeeId,
        decimal totalAmount,
        int bonusesSpent = 0,
        int bonusesEarned = 0,
        int? cardId = null,
        DateTime? timeStamp = null)
    {
        CardId = cardId;
        EmployeeId = employeeId;
        TotalAmount = totalAmount;
        BonusesSpent = bonusesSpent;
        BonusesEarned = bonusesEarned;
        TimeStamp = timeStamp ?? DateTime.UtcNow;
    }

    // Optional reference to the loyalty card used in the purchase.
    // Must be a positive ID when provided; null represents a purchase without a card.
    public int? CardId
    {
        get => _cardId;
        set
        {
            if (value.HasValue && !ValidationHelper.IsPositive(value.Value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    string.Format(CoreResources.PurchaseValCardIdPositive, value.Value));
            }

            _cardId = value;
        }
    }

    // References the employee who processed the purchase in EMPLOYEES.
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
                    string.Format(CoreResources.PurchaseValEmployeeIdPositive, value));
            }

            _employeeId = value;
        }
    }

    // Total monetary amount of the purchase.
    // Must be zero or greater according to PURCHASES schema.
    public decimal TotalAmount
    {
        get => _totalAmount;
        set
        {
            if (!ValidationHelper.IsNonNegative(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    string.Format(CoreResources.PurchaseValTotalAmountNonNegative, value));
            }

            _totalAmount = value;
        }
    }

    // Number of loyalty bonuses spent on this purchase.
    // Negative bonus counts are not allowed.
    public int BonusesSpent
    {
        get => _bonusesSpent;
        set
        {
            if (!ValidationHelper.IsNonNegative(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    string.Format(CoreResources.PurchaseValBonusesSpentNonNegative, value));
            }

            _bonusesSpent = value;
        }
    }

    // Number of loyalty bonuses earned from this purchase.
    // Negative bonus counts are not allowed.
    public int BonusesEarned
    {
        get => _bonusesEarned;
        set
        {
            if (!ValidationHelper.IsNonNegative(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    string.Format(CoreResources.PurchaseValBonusesEarnedNonNegative, value));
            }

            _bonusesEarned = value;
        }
    }

    // Date and time when the purchase occurred.
    // Defaults to current UTC time and rejects default DateTime values.
    public DateTime TimeStamp
    {
        get => _timeStamp;
        set
        {
            if (value == default)
            {
                throw new ArgumentException(
                    string.Format(CoreResources.PurchaseValTimeStampInvalid, value),
                    nameof(value));
            }

            _timeStamp = value;
        }
    }
}