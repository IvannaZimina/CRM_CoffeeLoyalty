using CoffeeLoyalty.Core.resources;

using CoffeeLoyalty.Core.Validation;

namespace CoffeeLoyalty.Core.Models;

// Represents a physical or virtual loyalty card owned by a customer.
// Each card has its own ID and can be activated or blocked independently.
public class LoyaltyCard : BaseEntity, IActivatable
{
    private int _customerId;

    // Creates a loyalty card linked to an existing customer.
    // New cards are active by default unless specified otherwise.
    public LoyaltyCard(int customerId, bool isActive = true)
    {
        CustomerId = customerId;
        IsActive = isActive;
    }

    // Stores the ID of the customer who owns this card.
    // A positive ID is required to reference a valid customer record.
    public int CustomerId
    {
        get => _customerId;
        set
        {
            if (!ValidationHelper.IsPositive(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    CoreResources.ValCardCustomerIdPositive);
            }

            _customerId = value;
        }
    }

    // Controls whether the card can be used.
    // Setting this to false blocks the card without deleting it.
    // This allows previous purchases and card history to be preserved.
    public bool IsActive { get; set; }
}
