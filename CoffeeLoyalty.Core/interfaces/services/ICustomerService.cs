
using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Models;

namespace CoffeeLoyalty.Core.Interfaces.Services;

// Defines customer and loyalty card management operations.
public interface ICustomerService
{
    // Retrieves all customers, including inactive accounts.
    Task<OperationResult<IReadOnlyList<Customer>>> GetAllCustomersAsync(
        CancellationToken cancellationToken = default);

    // Retrieves only active customer accounts.
    Task<OperationResult<IReadOnlyList<Customer>>> GetActiveCustomersAsync(
        CancellationToken cancellationToken = default);

    // Finds a customer by their database ID.
    Task<OperationResult<Customer>> GetCustomerByIdAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    // Searches customers by name or partial text.
    Task<OperationResult<IReadOnlyList<Customer>>> SearchCustomersAsync(
        string searchText,
        CancellationToken cancellationToken = default);

    // Finds the customer linked to a loyalty card.
    Task<OperationResult<Customer>> GetCustomerByCardAsync(
        int cardId,
        CancellationToken cancellationToken = default);

    // Retrieves all cards belonging to a customer.
    Task<OperationResult<IReadOnlyList<LoyaltyCard>>> GetCustomerCardsAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    // Registers a customer and creates their first card together.
    Task<OperationResult<LoyaltyCard>> RegisterCustomerAsync(
        Customer customer,
        CancellationToken cancellationToken = default);

    // Updates customer profile information.
    Task<OperationResult> UpdateCustomerAsync(
        Customer customer,
        CancellationToken cancellationToken = default);

    // Changes the customer's loyalty category.
    Task<OperationResult> ChangeCategoryAsync(
        int customerId,
        int categoryId,
        CancellationToken cancellationToken = default);

    // Issues an additional loyalty card to an existing customer.
    Task<OperationResult<LoyaltyCard>> IssueCardAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    // Activates or deactivates a customer account.
    Task<OperationResult> SetCustomerActiveStatusAsync(
        int customerId,
        bool isActive,
        CancellationToken cancellationToken = default);

    // Activates or blocks an existing loyalty card.
    Task<OperationResult> SetCardActiveStatusAsync(
        int cardId,
        bool isActive,
        CancellationToken cancellationToken = default);
}
