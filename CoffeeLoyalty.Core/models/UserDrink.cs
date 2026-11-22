using CoffeeLoyalty.Core.resources;
using CoffeeLoyalty.Core.Validation;

namespace CoffeeLoyalty.Core.Models;

// Represents an aggregated drink counter from the USER_DRINK table.
// Inherits the record ID from BaseEntity and tracks how many units of a drink are associated with a card.
public class UserDrink : BaseEntity
{
    private int _cardId;
    private int _drinkId;
    private int _quantity;

    // Creates a UserDrink counter record for a loyalty card and drink pair.
    public UserDrink(
        int cardId,
        int drinkId,
        int quantity)
    {
        CardId = cardId;
        DrinkId = drinkId;
        Quantity = quantity;
    }

    // References the loyalty card in CARDS associated with this counter.
    // Must be a positive integer identifier.
    public int CardId
    {
        get => _cardId;
        set
        {
            if (!ValidationHelper.IsPositive(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    string.Format(CoreResources.UserDrinkValCardIdPositive, nameof(CardId), value));
            }

            _cardId = value;
        }
    }

    // References the drink in DRINKS tracked by this counter.
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
                    string.Format(CoreResources.UserDrinkValDrinkIdPositive, nameof(DrinkId), value));
            }

            _drinkId = value;
        }
    }

    // Stores the drink quantity for this card and drink pair.
    // Must be greater than zero according to model validation rules.
    public int Quantity
    {
        get => _quantity;
        set
        {
            if (!ValidationHelper.IsPositive(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    string.Format(CoreResources.UserDrinkValQuantityPositive, nameof(Quantity), value));
            }

            _quantity = value;
        }
    }
}