using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Models;

namespace CoffeeLoyalty.Core.Interfaces.Repositories;

// Defines data access operations for menu drinks without exposing SQLite or UI types.
public interface IDrinkRepository
{
    // Retrieves all drinks for management views, including inactive items.
    Task<OperationResult<IReadOnlyList<Drink>>> GetAllAsync(
        CancellationToken cancellationToken = default);

    // Retrieves only currently active/available drinks for cashier sales.
    Task<OperationResult<IReadOnlyList<Drink>>> GetAvailableAsync(
        CancellationToken cancellationToken = default);

    // Retrieves a single drink by its identifier.
    Task<OperationResult<Drink>> GetByIdAsync(
        int drinkId,
        CancellationToken cancellationToken = default);

    // Adds a new drink to the menu and returns the created drink record.
    Task<OperationResult<Drink>> AddAsync(
        Drink drink,
        CancellationToken cancellationToken = default);

    // Adds a drink using the caller's transaction when supplied.
    Task<OperationResult<Drink>> AddAsync(
        Drink drink,
        object? transaction,
        CancellationToken cancellationToken = default);

    // Updates existing drink details such as name, price, type, and image path.
    Task<OperationResult> UpdateAsync(
        Drink drink,
        CancellationToken cancellationToken = default);

    // Updates the availability status (isActive) of a drink.
    Task<OperationResult> SetActiveStatusAsync(
        int drinkId,
        bool isActive,
        CancellationToken cancellationToken = default);
}