
namespace CoffeeLoyalty.Core.Models;

// Interface for entities that can be active or inactive.
public interface IActivatable
{
    // Shows whether the entity is active.
    bool IsActive { get; set; }
}
