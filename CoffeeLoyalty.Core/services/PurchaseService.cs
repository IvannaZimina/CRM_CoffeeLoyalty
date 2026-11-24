using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.interfaces.services;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Interfaces.Services;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.resources;
using CoffeeLoyalty.Core.Validation;
using Microsoft.Data.Sqlite;
using System.Diagnostics;

namespace CoffeeLoyalty.Core.Services;

// Coordinates checkout validation, atomic persistence, bonus updates and audit logging.
public class PurchaseService : IPurchaseService
{
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

    // Preserve the existing constructor contract. Checkout requires a connection factory.
    // All repositories must use the same database as this factory.
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

    // Audit only after the transaction and its connection have been released.
    public async Task<PurchaseResult> ProcessPurchaseAsync(
        PurchaseRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PurchaseResult result;
        try
        {
            result = await ProcessCoreAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await AuditAsync(true, LogActionType.ErrorPurchaseFailed, "Purchase cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("PurchaseService: {0}", ex.GetType().Name);
            result = PurchaseResult.Failure(
                CoreResources.ErrorPurchaseProcessingFailed, "PURCHASE_PROCESS_ERROR");
        }

        if (result.IsSuccess)
        {
            await AuditAsync(false, LogActionType.LogPurchase,
                $"Purchase #{result.PurchaseId} completed (Total: {result.TotalAmount:0.00}, Spent: {result.BonusesSpent}, Earned: {result.BonusesEarned}).",
                result.PurchaseId);
        }
        else
        {
            LogActionType action = result.ErrorCode switch
            {
                "UNAUTHENTICATED" => LogActionType.ErrorPurchaseUnauthorized,
                "EMPLOYEE_MISMATCH" => LogActionType.ErrorPurchaseForbidden,
                _ => LogActionType.ErrorPurchaseFailed
            };

            // Record a stable error code rather than arbitrary exception details.
            string description = result.ErrorCode switch
            {
                "UNAUTHENTICATED" => CoreResources.DrinkServiceAuthRequired,
                "EMPLOYEE_MISMATCH" => CoreResources.DrinkServiceAccessDenied,
                _ => $"{CoreResources.ErrorPurchaseProcessingFailed} ({result.ErrorCode ?? "PURCHASE_PROCESS_ERROR"})"
            };
            await AuditAsync(true, action, description);
        }

        return result;
    }

    // Validate the request and execute all checkout writes inside one transaction.
    private async Task<PurchaseResult> ProcessCoreAsync(
        PurchaseRequest request, CancellationToken cancellationToken)
    {
        if (!_authService.IsAuthenticated || _authService.CurrentEmployee is null)
            return PurchaseResult.Failure(CoreResources.DrinkServiceAuthRequired, "UNAUTHENTICATED");

        if (request is null)
            return PurchaseResult.Failure(CoreResources.ValRequiredField, "INVALID_REQUEST");

        if (!ValidationHelper.IsPositive(request.EmployeeId))
            return PurchaseResult.Failure(
                string.Format(CoreResources.PurchaseRequestValEmployeeIdPositive, nameof(request.EmployeeId), request.EmployeeId),
                "INVALID_EMPLOYEE_ID");

        if (request.EmployeeId != _authService.CurrentEmployee.Id)
            return PurchaseResult.Failure(CoreResources.DrinkServiceAccessDenied, "EMPLOYEE_MISMATCH");

        if (request.Items is null || request.Items.Count == 0)
            return PurchaseResult.Failure(CoreResources.PurchaseItemCollectionEmpty, "EMPTY_CART");

        // Never permit persistence without a transaction, even if the optional factory was omitted.
        if (_connectionFactory is null)
            return PurchaseResult.Failure(CoreResources.ErrorPurchaseProcessingFailed, "PURCHASE_PROCESS_ERROR");

        cancellationToken.ThrowIfCancellationRequested();
        await using SqliteConnection connection = _connectionFactory.OpenConnection();

        // Reserve the SQLite writer before reading prices, cards or balances.
        // Existing read methods use separate connections to the same database.
        // The reservation prevents another writer from changing those committed rows.
        // Disposing an uncommitted transaction rolls back all its writes.
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        cancellationToken.ThrowIfCancellationRequested();

        List<CartItem> lineItems = [];
        decimal orderSubtotal = 0m;

        // Resolve authoritative menu prices and verify every drink is available.
        foreach (CartItem item in request.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item is null || item.Drink is null || !ValidationHelper.IsPositive(item.Quantity))
                return PurchaseResult.Failure(CoreResources.ValRequiredField, "INVALID_CART_ITEM");

            if (!ValidationHelper.IsPositive(item.Drink.Id))
                return PurchaseResult.Failure(CoreResources.DrinkServiceIdPositive, "INVALID_DRINK_ID");

            OperationResult<Drink> drinkResult = await _drinkRepository.GetByIdAsync(item.Drink.Id, cancellationToken);
            if (!drinkResult.IsSuccess || drinkResult.Value is null)
                return PurchaseResult.Failure(
                    drinkResult.ErrorMessage ?? CoreResources.DrinkNotFound,
                    drinkResult.ErrorCode ?? "DRINK_NOT_FOUND");

            if (!drinkResult.Value.IsActive)
                return PurchaseResult.Failure(
                    string.Format(CoreResources.DrinkInactive, drinkResult.Value.DrinkName), "DRINK_INACTIVE");

            lineItems.Add(new CartItem(drinkResult.Value, item.Quantity));
            orderSubtotal += drinkResult.Value.DrinkPrice * item.Quantity;
        }

        // Load the latest committed card and customer under the writer reservation.
        LoyaltyCard? card = null;
        Customer? customer = null;
        string? customerName = null;

        if (request.CardId.HasValue)
        {
            if (!ValidationHelper.IsPositive(request.CardId.Value))
                return PurchaseResult.Failure(
                    string.Format(CoreResources.PurchaseRequestValCardIdPositive, nameof(request.CardId), request.CardId.Value),
                    "INVALID_CARD_ID");

            card = await _cardRepository.GetByIdAsync(request.CardId.Value, cancellationToken);
            if (card is null)
                return PurchaseResult.Failure(CoreResources.CardNotFound, "CARD_NOT_FOUND");
            if (!card.IsActive)
                return PurchaseResult.Failure(CoreResources.BonusCardInactive, "CARD_INACTIVE");

            customer = await _customerRepository.GetByIdAsync(card.CustomerId, cancellationToken);
            if (customer is null)
                return PurchaseResult.Failure(CoreResources.CustomerNotFound, "CUSTOMER_NOT_FOUND");
            if (!customer.IsActive)
                return PurchaseResult.Failure(CoreResources.BonusCustomerInactive, "CUSTOMER_INACTIVE");

            customerName = customer.CustomerName;
        }

        // Validate redemption before calculating the final amount and loyalty balance.
        int bonusesSpent = 0;
        if (request.BonusesToSpend > 0)
        {
            if (card is null || customer is null)
                return PurchaseResult.Failure(CoreResources.BonusCardRequiredForRedemption, "CARD_REQUIRED");

            OperationResult redemptionCheck = _bonusService.ValidateBonusRedemption(
                customer, card, request.BonusesToSpend, orderSubtotal);
            if (!redemptionCheck.IsSuccess)
                return PurchaseResult.Failure(
                    redemptionCheck.ErrorMessage ?? CoreResources.BonusRedemptionInvalid,
                    redemptionCheck.ErrorCode ?? "INVALID_BONUS_REDEMPTION");

            bonusesSpent = request.BonusesToSpend;
        }

        decimal finalAmount = _bonusService.CalculateFinalAmount(orderSubtotal, bonusesSpent);
        int bonusesEarned = (card is not null && customer is not null)
            ? _bonusService.CalculateEarnedBonuses(finalAmount, null) : 0;
        int updatedBalance = (card is not null && customer is not null)
            ? _bonusService.CalculateUpdatedBalance(customer.BonusBalance, bonusesSpent, bonusesEarned) : 0;

        // Persist the receipt header and line items using the same transaction.
        Purchase purchase = new(
            employeeId: request.EmployeeId,
            totalAmount: finalAmount,
            bonusesSpent: bonusesSpent,
            bonusesEarned: bonusesEarned,
            cardId: card?.Id,
            timeStamp: DateTime.UtcNow);

        OperationResult<Purchase> addPurchaseResult = await _purchaseRepository.AddAsync(
            purchase, transaction, cancellationToken);
        if (!addPurchaseResult.IsSuccess || addPurchaseResult.Value is null || addPurchaseResult.Value.Id <= 0)
            return PurchaseResult.Failure(
                addPurchaseResult.ErrorMessage ?? CoreResources.PurchaseSaveFailed,
                addPurchaseResult.ErrorCode ?? "PURCHASE_SAVE_FAILED");

        int purchaseId = addPurchaseResult.Value.Id;
        List<PurchaseItem> receiptItems = lineItems
            .Select(ci => new PurchaseItem(
                purchaseId: purchaseId,
                drinkId: ci.Drink.Id,
                quantity: ci.Quantity,
                unitPrice: ci.Drink.DrinkPrice))
            .ToList();

        OperationResult addItemsResult = await _purchaseItemRepository.AddRangeAsync(
            receiptItems, transaction, cancellationToken);
        if (!addItemsResult.IsSuccess)
            return PurchaseResult.Failure(
                addItemsResult.ErrorMessage ?? CoreResources.PurchaseItemsSaveFailed,
                addItemsResult.ErrorCode ?? "PURCHASE_ITEMS_SAVE_FAILED");

        // Walk-in purchases skip all loyalty updates.
        if (card is not null && customer is not null)
        {
            foreach (CartItem ci in lineItems)
            {
                OperationResult<UserDrink> upsertResult = await _userDrinkRepository.UpsertQuantityAsync(
                    card.Id, ci.Drink.Id, ci.Quantity, transaction, cancellationToken);
                if (!upsertResult.IsSuccess)
                    return PurchaseResult.Failure(
                        upsertResult.ErrorMessage ?? CoreResources.UserDrinkUpdateFailed,
                        upsertResult.ErrorCode ?? "USER_DRINK_UPDATE_FAILED");
            }

            customer.BonusBalance = updatedBalance;
            bool customerUpdated = await _customerRepository.UpdateAsync(
                customer, transaction, cancellationToken);
            if (!customerUpdated)
                return PurchaseResult.Failure(CoreResources.ErrorCustomerOperationFailed, "CUSTOMER_UPDATE_FAILED");
        }

        // Prepare the successful result before committing so result validation cannot fail after commit.
        PurchaseResult result = PurchaseResult.Success(
            purchaseId: purchaseId,
            totalAmount: finalAmount,
            bonusesSpent: bonusesSpent,
            bonusesEarned: bonusesEarned,
            updatedBonusBalance: updatedBalance,
            items: lineItems,
            customerName: customerName);

        // Honor cancellation before commit. Once commit starts, finish it without cancellation.
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None);
        return result;
    }

    // Audit is best-effort and must never change the outcome of a completed checkout.
    private async Task AuditAsync(bool error, LogActionType action, string description, int? objectId = null)
    {
        try
        {
            OperationResult result = error
                ? await _logService.LogErrorAsync(AuditObjectType, action, description, objectId, CancellationToken.None)
                : await _logService.LogInfoAsync(AuditObjectType, action, description, objectId, CancellationToken.None);
            if (result.IsFailure)
                Trace.TraceWarning("PurchaseService: audit write failed.");
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("PurchaseService audit: {0}", ex.GetType().Name);
        }
    }
}
