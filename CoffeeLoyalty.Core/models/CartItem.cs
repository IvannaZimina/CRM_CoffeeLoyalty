using CoffeeLoyalty.Core.resources;
using CoffeeLoyalty.Core.Validation;

namespace CoffeeLoyalty.Core.Models;

// Represents an in-memory cart line item pairing a selected Drink with its requested quantity.
// Does not inherit from BaseEntity because cart items exist only before checkout is finalized.
public class CartItem
{
    private Drink _drink = null!;
    private int _quantity;

    // Creates a cart item for a selected drink and quantity.
    public CartItem(Drink drink, int quantity = 1)
    {
        Drink = drink;
        Quantity = quantity;
    }

    // Selected drink menu item in the cart.
    // Cannot be null.
    public Drink Drink
    {
        get => _drink;
        set
        {
            if (value is null)
            {
                throw new ArgumentNullException(
                    nameof(value),
                    string.Format(CoreResources.CartItemValDrinkRequired, nameof(Drink)));
            }

            _drink = value;
        }
    }

    // Number of drink units selected in the cart.
    // Must be greater than zero.
    public int Quantity
    {
        get => _quantity;
        set
        {
            if (!ValidationHelper.IsPositive(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    string.Format(CoreResources.CartItemValQuantityPositive, nameof(Quantity), value));
            }

            _quantity = value;
        }
    }

    // Computed line subtotal calculated from the drink price and quantity.
    public decimal Subtotal => Drink.DrinkPrice * Quantity;
}