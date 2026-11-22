using CoffeeLoyalty.Core.resources;
using CoffeeLoyalty.Core.Validation;

namespace CoffeeLoyalty.Core.Models;

// Represents a menu drink item from the DRINKS table.
// Inherits the drink ID from BaseEntity and supports menu availability via IActivatable.
public class Drink : BaseEntity, IActivatable
{
    private string _drinkName = string.Empty;
    private decimal _drinkPrice;
    private string _drinkType = string.Empty;
    private string? _drinkImageLink;

    // Creates a drink menu item with required name, price, and type.
    // New drinks are active by default unless specified otherwise.
    public Drink(
        string drinkName,
        decimal drinkPrice,
        string drinkType,
        bool isActive = true,
        string? drinkImageLink = null)
    {
        DrinkName = drinkName;
        DrinkPrice = drinkPrice;
        DrinkType = drinkType;
        IsActive = isActive;
        DrinkImageLink = drinkImageLink;
    }

    // Stores the display name of the drink without leading or trailing spaces.
    // Empty or whitespace-only names are not allowed.
    public string DrinkName
    {
        get => _drinkName;
        set
        {
            if (!ValidationHelper.HasText(value))
            {
                throw new ArgumentException(
                    string.Format(CoreResources.DrinkValNameRequired, nameof(DrinkName)),
                    nameof(value));
            }

            _drinkName = value.Trim();
        }
    }

    // Stores the price of the drink.
    // Matches DRINKS schema constraint requiring a positive price.
    public decimal DrinkPrice
    {
        get => _drinkPrice;
        set
        {
            if (!ValidationHelper.IsPositive(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    string.Format(CoreResources.DrinkValPricePositive, nameof(DrinkPrice), value));
            }

            _drinkPrice = value;
        }
    }

    // Controls whether the drink is currently available in the menu.
    // Inactive drinks can remain in the database to preserve historical receipts.
    public bool IsActive { get; set; }

    // Stores the drink category or type without leading or trailing spaces.
    // Empty or whitespace-only values are not allowed.
    public string DrinkType
    {
        get => _drinkType;
        set
        {
            if (!ValidationHelper.HasText(value))
            {
                throw new ArgumentException(
                    string.Format(CoreResources.DrinkValTypeRequired, nameof(DrinkType)),
                    nameof(value));
            }

            _drinkType = value.Trim();
        }
    }

    // Optional relative path or image reference for the drink.
    // Whitespace-only strings are normalized to null.
    public string? DrinkImageLink
    {
        get => _drinkImageLink;
        set => _drinkImageLink = string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}