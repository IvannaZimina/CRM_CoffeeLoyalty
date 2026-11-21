
namespace CoffeeLoyalty.Core.Models;

// Base class for database entities with an ID.
public abstract class BaseEntity
{
    // Unique identifier of an entity.
    public int Id { get; set; }
}
