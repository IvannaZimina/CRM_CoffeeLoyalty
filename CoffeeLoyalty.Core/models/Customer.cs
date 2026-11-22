
using CoffeeLoyalty.Core.Validation;

namespace CoffeeLoyalty.Core.Models;

// Represents a coffee shop customer and their loyalty information.
// Inherits the Id property from BaseEntity and supports active/inactive status.
public class Customer : BaseEntity, IActivatable
{
    private int _categoryId;
    private string _customerName = string.Empty;
    private int _bonusBalance;
    private DateTime _timeStamp;

    // Creates a customer with a required category and name.
    // New customers start with zero bonuses and an active account by default.
    public Customer(
        int categoryId,
        string customerName,
        int bonusBalance = 0,
        DateOnly? birthDate = null,
        bool isActive = true,
        DateTime? timeStamp = null)
    {
        CategoryId = categoryId;
        CustomerName = customerName;
        BonusBalance = bonusBalance;
        BirthDate = birthDate;
        IsActive = isActive;
        TimeStamp = timeStamp ?? DateTime.UtcNow;
    }

    // References the customer's loyalty category in the CATEGORIES table.
    // The ID must be positive because zero or negative IDs are not valid.
    public int CategoryId
    {
        get => _categoryId;
        set
        {
            if (!ValidationHelper.IsPositive(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Category ID must be positive.");
            }

            _categoryId = value;
        }
    }

    // Stores the customer's name without leading or trailing spaces.
    // Empty or whitespace-only names are not allowed.
    public string CustomerName
    {
        get => _customerName;
        set
        {
            if (!ValidationHelper.HasText(value))
            {
                throw new ArgumentException(
                    "Customer name cannot be empty.",
                    nameof(value));
            }

            _customerName = value.Trim();
        }
    }

    // Represents the customer's available loyalty bonuses.
    // The balance cannot become negative.
    public int BonusBalance
    {
        get => _bonusBalance;
        set
        {
            if (!ValidationHelper.IsNonNegative(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Bonus balance cannot be negative.");
            }

            _bonusBalance = value;
        }
    }

    // Optional date of birth. DateOnly is used because
    // a birthday does not need a time of day.
    public DateOnly? BirthDate { get; set; }

    // Allows customer accounts to be deactivated without deleting them.
    // This helps preserve purchase and loyalty history.
    public bool IsActive { get; set; }

    // Stores the customer's registration timestamp.
    // New records use the current UTC time unless another value is supplied.
    public DateTime TimeStamp
    {
        get => _timeStamp;
        set
        {
            if (value == default)
            {
                throw new ArgumentException(
                    "Registration timestamp must be valid.",
                    nameof(value));
            }

            _timeStamp = value;
        }
    }
}
