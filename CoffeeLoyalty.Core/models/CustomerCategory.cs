
using CoffeeLoyalty.Core.Validation;

namespace CoffeeLoyalty.Core.Models;

/// Represents a loyalty category such as Basic, Silver, or Gold.
/// Validates category names, bonus rates, and upgrade thresholds.
public class CustomerCategory : BaseEntity
{
    private string _categoryName = string.Empty;
    private decimal _bonusAccrualRate;
    private int _upgradeThreshold;

    // Creates a category with valid initial values.
    public CustomerCategory(
        string categoryName,
        decimal bonusAccrualRate = 0m,
        int upgradeThreshold = 0)
    {
        CategoryName = categoryName;
        BonusAccrualRate = bonusAccrualRate;
        UpgradeThreshold = upgradeThreshold;
    }

    // Name of the loyalty tier.
    public string CategoryName
    {
        get => _categoryName;
        set
        {
            if (!ValidationHelper.HasText(value))
            {
                throw new ArgumentException(
                    "Category name cannot be empty.",
                    nameof(value));
            }

            _categoryName = value.Trim();
        }
    }

    /// Gets or sets the bonus accrual rate.
    /// Negative values are not allowed.
    public decimal BonusAccrualRate
    {
        get => _bonusAccrualRate;
        set
        {
            if (!ValidationHelper.IsNonNegative(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Bonus accrual rate cannot be negative.");
            }

            _bonusAccrualRate = value;
        }
    }

    // Minimum threshold required to reach this category.
    public int UpgradeThreshold
    {
        get => _upgradeThreshold;
        set
        {
            if (!ValidationHelper.IsNonNegative(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Upgrade threshold cannot be negative.");
            }

            _upgradeThreshold = value;
        }
    }
}
