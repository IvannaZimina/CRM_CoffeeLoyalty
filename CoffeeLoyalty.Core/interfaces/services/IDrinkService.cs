using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Models;

namespace CoffeeLoyalty.Core.Interfaces.Services;

// Defines drink menu management and menu query operations without exposing repository or UI types.
public interface IDrinkService
{
    // Retrieves all drinks, including inactive or archived items.
    Task<OperationResult<IReadOnlyList<Drink>>> GetAllDrinksAsync(
        CancellationToken cancellationToken = default);

    // Retrieves only active/available drinks for POS checkout and menu selection.
    Task<OperationResult<IReadOnlyList<Drink>>> GetAvailableDrinksAsync(
        CancellationToken cancellationToken = default);

    // Retrieves a single drink by its identifier.
    Task<OperationResult<Drink>> GetDrinkByIdAsync(
        int drinkId,
        CancellationToken cancellationToken = default);

    // Retrieves drinks filtered by category or type.
    Task<OperationResult<IReadOnlyList<Drink>>> GetDrinksByTypeAsync(
        string drinkType,
        CancellationToken cancellationToken = default);

    // Searches drinks matching the specified search text.
    Task<OperationResult<IReadOnlyList<Drink>>> SearchDrinksAsync(
        string searchText,
        CancellationToken cancellationToken = default);

    // Adds a new drink to the menu and returns the created drink record.
    Task<OperationResult<Drink>> AddDrinkAsync(
        Drink drink,
        CancellationToken cancellationToken = default);

    // Updates existing drink details such as name, price, type, and relative image path.
    Task<OperationResult> UpdateDrinkAsync(
        Drink drink,
        CancellationToken cancellationToken = default);

    // Activates or deactivates a drink in the menu.
    Task<OperationResult> SetActiveStatusAsync(
        int drinkId,
        bool isActive,
        CancellationToken cancellationToken = default);
}