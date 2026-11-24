using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.interfaces.services;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Interfaces.Services;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.resources;
using CoffeeLoyalty.Core.Validation;
using Microsoft.Data.Sqlite;

namespace CoffeeLoyalty.Core.Services;

// Coordinates the complete checkout workflow: validates the active employee session, cart items,
// loyalty card/customer eligibility, and bonus redemption rules; calculates totals and bonuses
// via IBonusService; persists receipt headers, line items, drink counters, and customer balances
// atomically in SQLite; and records audit logs via ILogService.
public class PurchaseService : IPurchaseService
{
    // Audit log object type used when recording checkout events and errors via ILogService.
    private const string AuditObjectType = "Purchase";

    private readonly IPurchaseRepository _purchaseRepository;
    private readonly IPurchaseItemRepository _purchaseItemRepository;
    private readonly IUserDrinkRepository _userDrinkRepository;
    private readonly IDrinkRepository _drinkRepository;
    private readonly ICardRepository _cardRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IBonusService _bonusService;
    private readonly IAuthService _authService;
    private readonly ILogService _logService;
    private readonly SqliteConnectionFactory? _connectionFactory;

    // Initializes PurchaseService with required repositories, services, and optional connection factory
    // for running all checkout writes inside a single SQLite transaction.
    public PurchaseService(
        IPurchaseRepository purchaseRepository,
        IPurchaseItemRepository purchaseItemRepository,
        IUserDrinkRepository userDrinkRepository,
        IDrinkRepository drinkRepository,
        ICardRepository cardRepository,
        ICustomerRepository customerRepository,
        IBonusService bonusService,
        IAuthService authService,
        ILogService logService,
        SqliteConnectionFactory? connectionFactory = null)
    {
        // Guard all required dependencies so missing services or repositories fail immediately.
        _purchaseRepository = purchaseRepository ?? throw new ArgumentNullException(nameof(purchaseRepository));
        _purchaseItemRepository = purchaseItemRepository ?? throw new ArgumentNullException(nameof(purchaseItemRepository));
        _userDrinkRepository = userDrinkRepository ?? throw new ArgumentNullException(nameof(userDrinkRepository));
        _drinkRepository = drinkRepository ?? throw new ArgumentNullException(nameof(drinkRepository));
        _cardRepository = cardRepository ?? throw new ArgumentNullException(nameof(cardRepository));
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _bonusService = bonusService ?? throw new ArgumentNullException(nameof(bonusService));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _connectionFactory = connectionFactory;
    }

    // Validates and processes a checkout request atomically, returning PurchaseResult with final
    // totals, spent/earned bonuses, and updated balance on success, or error details on failure.
    public async Task<PurchaseResult> ProcessPurchaseAsync(
        PurchaseRequest request,
        CancellationToken cancellationToken = default)
    {
        // Stop early if cancellation was requested before starting checkout.
        cancellationToken.ThrowIfCancellationRequested();

        // Step 1: Ensure an employee is signed in; checkout cannot run without an active session.
        if (!_authService.IsAuthenticated || _authService.CurrentEmployee is null)
        {
            await _logService.LogErrorAsync(
                AuditObjectType,
                LogActionType.ErrorPurchaseUnauthorized,
                CoreResources.DrinkServiceAuthRequired,
                null,
                cancellationToken);

            return PurchaseResult.Failure(
                CoreResources.DrinkServiceAuthRequired,
                "UNAUTHENTICATED");
        }

        // Step 2: Validate top-level request object, employee ID, session match, and non-empty cart.
        if (request is null)
        {
            return PurchaseResult.Failure(CoreResources.ValRequiredField, "INVALID_REQUEST");
        }

        if (!ValidationHelper.IsPositive(request.EmployeeId))
        {
            return PurchaseResult.Failure(
                string.Format(CoreResources.PurchaseRequestValEmployeeIdPositive, nameof(request.EmployeeId), request.EmployeeId),
                "INVALID_EMPLOYEE_ID");
        }

        // Ensure the request's EmployeeId matches the authenticated employee in the active session.
        if (request.EmployeeId != _authService.CurrentEmployee.Id)
        {
            await _logService.LogErrorAsync(
                AuditObjectType,
                LogActionType.ErrorPurchaseForbidden,
                CoreResources.DrinkServiceAccessDenied,
                null,
                cancellationToken);

            return PurchaseResult.Failure(
                CoreResources.DrinkServiceAccessDenied,
                "EMPLOYEE_MISMATCH");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return PurchaseResult.Failure(
                CoreResources.PurchaseItemCollectionEmpty,
                "EMPTY_CART");
        }

        // Step 3: Validate each cart item, verify the drink exists and is active in the menu,
        // and calculate the pre-discount subtotal using authoritative DB drink prices.
        List<CartItem> lineItems = [];
        decimal orderSubtotal = 0m;

        foreach (CartItem item in request.Items)
        {
            if (item is null || item.Drink is null || !ValidationHelper.IsPositive(item.Quantity))
            {
                return PurchaseResult.Failure(CoreResources.ValRequiredField, "INVALID_CART_ITEM");
            }

            if (!ValidationHelper.IsPositive(item.Drink.Id))
            {
                return PurchaseResult.Failure(CoreResources.DrinkServiceIdPositive, "INVALID_DRINK_ID");
            }

            // Re-read the drink from the database so stale or client-supplied prices cannot be used.
            OperationResult<Drink> drinkResult = await _drinkRepository.GetByIdAsync(item.Drink.Id, cancellationToken);
            if (!drinkResult.IsSuccess || drinkResult.Value is null)
            {
                return PurchaseResult.Failure(
                    drinkResult.ErrorMessage ?? CoreResources.DrinkNotFound,
                    drinkResult.ErrorCode ?? "DRINK_NOT_FOUND");
            }

            // Reject inactive menu drinks so discontinued items cannot be sold.
            if (!drinkResult.Value.IsActive)
            {
                return PurchaseResult.Failure(
                    string.Format(CoreResources.DrinkInactive, drinkResult.Value.DrinkName),
                    "DRINK_INACTIVE");
            }

            lineItems.Add(new CartItem(drinkResult.Value, item.Quantity));
            orderSubtotal += drinkResult.Value.DrinkPrice * item.Quantity;
        }

        // Step 4: If a card ID is provided, load and verify both the loyalty card and its customer.
        // Without CardId, the order is processed as a walk-in purchase without loyalty updates.
        LoyaltyCard? card = null;
        Customer? customer = null;
        string? customerName = null;

        if (request.CardId.HasValue)
        {
            if (!ValidationHelper.IsPositive(request.CardId.Value))
            {
                return PurchaseResult.Failure(
                    string.Format(CoreResources.PurchaseRequestValCardIdPositive, nameof(request.CardId), request.CardId.Value),
                    "INVALID_CARD_ID");
            }

            card = await _cardRepository.GetByIdAsync(request.CardId.Value, cancellationToken);
            if (card is null)
            {
                return PurchaseResult.Failure(CoreResources.CardNotFound, "CARD_NOT_FOUND");
            }

            if (!card.IsActive)
            {
                return PurchaseResult.Failure(CoreResources.BonusCardInactive, "CARD_INACTIVE");
            }

            customer = await _customerRepository.GetByIdAsync(card.CustomerId, cancellationToken);
            if (customer is null)
            {
                return PurchaseResult.Failure(CoreResources.CustomerNotFound, "CUSTOMER_NOT_FOUND");
            }

            if (!customer.IsActive)
            {
                return PurchaseResult.Failure(CoreResources.BonusCustomerInactive, "CUSTOMER_INACTIVE");
            }

            customerName = customer.CustomerName;
        }

        // Step 5: Validate bonus redemption against card/customer eligibility and order subtotal.
        int bonusesSpent = 0;
        if (request.BonusesToSpend > 0)
        {
            // Redeeming bonuses requires a valid active loyalty card and active customer.
            if (card is null || customer is null)
            {
                return PurchaseResult.Failure(
                    CoreResources.BonusCardRequiredForRedemption,
                    "CARD_REQUIRED");
            }

            OperationResult redemptionCheck = _bonusService.ValidateBonusRedemption(
                customer,
                card,
                request.BonusesToSpend,
                orderSubtotal);

            if (!redemptionCheck.IsSuccess)
            {
                return PurchaseResult.Failure(
                    redemptionCheck.ErrorMessage ?? CoreResources.BonusRedemptionInvalid,
                    redemptionCheck.ErrorCode ?? "INVALID_BONUS_REDEMPTION");
            }

            bonusesSpent = request.BonusesToSpend;
        }

        // Step 6: Calculate final payable amount, earned bonuses, and updated customer balance.
        // Walk-in orders without a card/customer earn 0 bonuses and do not update a customer balance.
        decimal finalAmount = _bonusService.CalculateFinalAmount(orderSubtotal, bonusesSpent);
        int bonusesEarned = (card is not null && customer is not null)
            ? _bonusService.CalculateEarnedBonuses(finalAmount, null)
            : 0;

        int updatedBalance = (card is not null && customer is not null)
            ? _bonusService.CalculateUpdatedBalance(customer.BonusBalance, bonusesSpent, bonusesEarned)
            : 0;

        DateTime timestamp = DateTime.UtcNow;

        SqliteConnection? connection = null;
        SqliteTransaction? transaction = null;

        try
        {
            // Step 7: Open a single SQLite transaction when a connection factory is provided so all
            // purchase writes commit together or roll back cleanly on any failure.
            if (_connectionFactory is not null)
            {
                connection = _connectionFactory.OpenConnection();
                transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            }

            // Insert the receipt header into PURCHASES and obtain the generated purchaseId.
            Purchase purchase = new(
                employeeId: request.EmployeeId,
                totalAmount: finalAmount,
                bonusesSpent: bonusesSpent,
                bonusesEarned: bonusesEarned,
                cardId: card?.Id,
                timeStamp: timestamp);

            OperationResult<Purchase> addPurchaseResult = await _purchaseRepository.AddAsync(
                purchase,
                transaction,
                cancellationToken);

            if (!addPurchaseResult.IsSuccess || addPurchaseResult.Value is null || addPurchaseResult.Value.Id <= 0)
            {
                await RollbackSafeAsync(transaction);
                return PurchaseResult.Failure(
                    addPurchaseResult.ErrorMessage ?? CoreResources.PurchaseSaveFailed,
                    addPurchaseResult.ErrorCode ?? "PURCHASE_SAVE_FAILED");
            }

            int purchaseId = addPurchaseResult.Value.Id;

            // Insert all receipt line items into PURCHASE_ITEM with unit prices from the menu.
            List<PurchaseItem> receiptItems = lineItems
                .Select(ci => new PurchaseItem(
                    purchaseId: purchaseId,
                    drinkId: ci.Drink.Id,
                    quantity: ci.Quantity,
                    unitPrice: ci.Drink.DrinkPrice))
                .ToList();

            OperationResult addItemsResult = await _purchaseItemRepository.AddRangeAsync(
                receiptItems,
                transaction,
                cancellationToken);

            if (!addItemsResult.IsSuccess)
            {
                await RollbackSafeAsync(transaction);
                return PurchaseResult.Failure(
                    addItemsResult.ErrorMessage ?? CoreResources.PurchaseItemsSaveFailed,
                    addItemsResult.ErrorCode ?? "PURCHASE_ITEMS_SAVE_FAILED");
            }

            // When a loyalty card is used, increment per-drink counters on that specific card
            // and save the customer's updated bonus balance.
            if (card is not null && customer is not null)
            {
                foreach (CartItem ci in lineItems)
                {
                    OperationResult<UserDrink> upsertResult = await _userDrinkRepository.UpsertQuantityAsync(
                        cardId: card.Id,
                        drinkId: ci.Drink.Id,
                        quantityDelta: ci.Quantity,
                        transaction: transaction,
                        cancellationToken: cancellationToken);

                    if (!upsertResult.IsSuccess)
                    {
                        await RollbackSafeAsync(transaction);
                        return PurchaseResult.Failure(
                            upsertResult.ErrorMessage ?? CoreResources.UserDrinkUpdateFailed,
                            upsertResult.ErrorCode ?? "USER_DRINK_UPDATE_FAILED");
                    }
                }

                customer.BonusBalance = updatedBalance;
                bool customerUpdated = await _customerRepository.UpdateAsync(
                    customer,
                    transaction,
                    cancellationToken);

                if (!customerUpdated)
                {
                    await RollbackSafeAsync(transaction);
                    return PurchaseResult.Failure(
                        CoreResources.ErrorCustomerOperationFailed,
                        "CUSTOMER_UPDATE_FAILED");
                }
            }

            // Commit all database changes once every checkout step succeeds.
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            // Write audit info after committing so LogService uses a separate connection without lock contention.
            await _logService.LogInfoAsync(
                AuditObjectType,
                LogActionType.LogPurchase,
                $"Purchase #{purchaseId} completed (Total: {finalAmount:0.00}, Spent: {bonusesSpent}, Earned: {bonusesEarned}).",
                purchaseId,
                cancellationToken);

            // Step 8: Return finalized receipt summary to the caller/UI layer.
            return PurchaseResult.Success(
                purchaseId: purchaseId,
                totalAmount: finalAmount,
                bonusesSpent: bonusesSpent,
                bonusesEarned: bonusesEarned,
                updatedBonusBalance: updatedBalance,
                items: lineItems,
                customerName: customerName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Roll back partial changes if cancelled mid-checkout, then rethrow.
            await RollbackSafeAsync(transaction);
            throw;
        }
        catch (Exception ex)
        {
            // Roll back any uncommitted SQLite changes and log the failure.
            await RollbackSafeAsync(transaction);

            await _logService.LogErrorAsync(
                AuditObjectType,
                LogActionType.ErrorPurchaseFailed,
                ex.Message,
                null,
                cancellationToken);

            return PurchaseResult.Failure(ex.Message, "PURCHASE_PROCESS_ERROR");
        }
        finally
        {
            // Dispose transaction and connection objects after checkout finishes.
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }

            if (connection is not null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    // Safely rolls back an active transaction without hiding the original failure.
    private static async Task RollbackSafeAsync(SqliteTransaction? transaction)
    {
        if (transaction is null)
        {
            return;
        }

        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // Preserve the original failure reason.
        }
    }
}