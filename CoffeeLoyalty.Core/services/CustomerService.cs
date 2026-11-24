using CoffeeLoyalty.Core.Data;
using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.Interfaces.Repositories;
using CoffeeLoyalty.Core.Interfaces.Services;
using CoffeeLoyalty.Core.interfaces.services;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.resources;
using Microsoft.Data.Sqlite;
using System.Diagnostics;

namespace CoffeeLoyalty.Core.Services;

// Coordinates customer profiles, loyalty cards, permissions and audit events.
public sealed class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _customers;
    private readonly ICardRepository _cards;
    private readonly ICategoryRepository _categories;
    private readonly IAuthService _auth;
    private readonly ILogService _logs;
    private readonly SqliteConnectionFactory _connectionFactory;

    // All repositories and the transaction factory must use the same database.
    public CustomerService(
        ICustomerRepository customers, ICardRepository cards,
        ICategoryRepository categories, IAuthService auth,
        ILogService logs, SqliteConnectionFactory connectionFactory)
    {
        _customers = customers ?? throw new ArgumentNullException(nameof(customers));
        _cards = cards ?? throw new ArgumentNullException(nameof(cards));
        _categories = categories ?? throw new ArgumentNullException(nameof(categories));
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _logs = logs ?? throw new ArgumentNullException(nameof(logs));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    // ==========================================
    // CUSTOMER AND CARD LOOKUP
    // ==========================================

    // Returns all customer accounts, including inactive ones.
    public Task<OperationResult<IReadOnlyList<Customer>>> GetAllCustomersAsync(
        CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<Customer>>(nameof(GetAllCustomersAsync), async () =>
            OperationResult<IReadOnlyList<Customer>>.Success(await _customers.GetAllAsync(cancellationToken)), cancellationToken);

    // Returns active customer accounts only.
    public Task<OperationResult<IReadOnlyList<Customer>>> GetActiveCustomersAsync(
        CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<Customer>>(nameof(GetActiveCustomersAsync), async () =>
            OperationResult<IReadOnlyList<Customer>>.Success(
                (await _customers.GetAllAsync(cancellationToken)).Where(c => c.IsActive).ToList().AsReadOnly()), cancellationToken);

    // Finds a customer by a positive database ID.
    public Task<OperationResult<Customer>> GetCustomerByIdAsync(
        int customerId, CancellationToken cancellationToken = default) =>
        ReadAsync<Customer>(nameof(GetCustomerByIdAsync), async () =>
        {
            if (customerId <= 0) return Invalid<Customer>();
            Customer? customer = await _customers.GetByIdAsync(customerId, cancellationToken);
            return customer is null ? NotFound<Customer>() : OperationResult<Customer>.Success(customer);
        }, cancellationToken);

    // Searches customer names using case-insensitive partial matching.
    public Task<OperationResult<IReadOnlyList<Customer>>> SearchCustomersAsync(
        string searchText, CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<Customer>>(nameof(SearchCustomersAsync), async () =>
        {
            if (string.IsNullOrWhiteSpace(searchText)) return Invalid<IReadOnlyList<Customer>>();
            string query = searchText.Trim();
            IReadOnlyList<Customer> customers = await _customers.GetAllAsync(cancellationToken);
            return OperationResult<IReadOnlyList<Customer>>.Success(customers
                .Where(c => c.CustomerName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList().AsReadOnly());
        }, cancellationToken);

    // Finds the owner even for blocked cards; checkout validates active status separately.
    public Task<OperationResult<Customer>> GetCustomerByCardAsync(
        int cardId, CancellationToken cancellationToken = default) =>
        ReadAsync<Customer>(nameof(GetCustomerByCardAsync), async () =>
        {
            if (cardId <= 0) return Invalid<Customer>();
            Customer? customer = await _customers.GetByCardAsync(cardId, cancellationToken);
            return customer is null ? NotFound<Customer>() : OperationResult<Customer>.Success(customer);
        }, cancellationToken, "CARDS", cardId);

    // Returns all cards belonging to an existing customer.
    public Task<OperationResult<IReadOnlyList<LoyaltyCard>>> GetCustomerCardsAsync(
        int customerId, CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<LoyaltyCard>>(nameof(GetCustomerCardsAsync), async () =>
        {
            if (customerId <= 0) return Invalid<IReadOnlyList<LoyaltyCard>>();
            if (await _customers.GetByIdAsync(customerId, cancellationToken) is null)
                return NotFound<IReadOnlyList<LoyaltyCard>>();
            return OperationResult<IReadOnlyList<LoyaltyCard>>.Success(
                await _cards.GetByCustomerIdAsync(customerId, cancellationToken));
        }, cancellationToken);

    // ==========================================
    // REGISTRATION AND CARD ISSUANCE
    // ==========================================

    // Registers a customer using the configured initial category.
    public Task<OperationResult<LoyaltyCard>> RegisterCustomerAsync(
        string customerName, DateOnly? birthDate = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync<OperationResult<LoyaltyCard>>(UserRole.Cashier, nameof(RegisterCustomerAsync), "CUSTOMERS", null,
            OperationResult<LoyaltyCard>.Failure, async () =>
            {
                if (string.IsNullOrWhiteSpace(customerName) || !ValidBirthDate(birthDate))
                    return Invalid<LoyaltyCard>();
                CustomerCategory? initial = await FindInitialCategoryAsync(cancellationToken);
                if (initial is null) return MissingCategory<LoyaltyCard>();
                Customer customer = new(initial.Id, customerName, birthDate: birthDate);
                return await RegisterCoreAsync(customer, cancellationToken);
            }, cancellationToken);

    // Registers an explicitly supplied customer without bypassing role or balance rules.
    public Task<OperationResult<LoyaltyCard>> RegisterCustomerAsync(
        Customer customer, CancellationToken cancellationToken = default) =>
        ExecuteAsync<OperationResult<LoyaltyCard>>(UserRole.Cashier, nameof(RegisterCustomerAsync), "CUSTOMERS", null,
            OperationResult<LoyaltyCard>.Failure, async () =>
            {
                if (customer is null || customer.Id != 0 || customer.BonusBalance != 0 ||
                    !customer.IsActive || !ValidBirthDate(customer.BirthDate))
                    return Invalid<LoyaltyCard>();
                CustomerCategory? category = await _categories.GetByIdAsync(customer.CategoryId, cancellationToken);
                if (category is null) return MissingCategory<LoyaltyCard>();
                if (!_auth.HasPermission(UserRole.Manager))
                {
                    CustomerCategory? initial = await FindInitialCategoryAsync(cancellationToken);
                    if (initial is null) return MissingCategory<LoyaltyCard>();
                    if (category.Id != initial.Id)
                        return OperationResult<LoyaltyCard>.Failure(CoreResources.ErrorCustomerAccessDenied, "ACCESS_DENIED");
                }
                return await RegisterCoreAsync(customer, cancellationToken);
            }, cancellationToken);

    // Creates the customer and first card in one transaction; publish IDs only after commit.
    private async Task<OperationResult<LoyaltyCard>> RegisterCoreAsync(Customer customer, CancellationToken token)
    {
        Customer pending = new(customer.CategoryId, customer.CustomerName, birthDate: customer.BirthDate);
        LoyaltyCard card;
        int customerId;
        await using (SqliteConnection connection = _connectionFactory.OpenConnection())
        await using (SqliteTransaction transaction = connection.BeginTransaction())
        {
            customerId = await _customers.AddAsync(pending, transaction, token);
            if (customerId <= 0) throw new InvalidOperationException();
            card = new LoyaltyCard(customerId);
            int cardId = await _cards.AddAsync(card, transaction, token);
            if (cardId <= 0) throw new InvalidOperationException();
            card.Id = cardId;
            token.ThrowIfCancellationRequested();
            await transaction.CommitAsync(token);
        }
        customer.Id = customerId;
        customer.TimeStamp = pending.TimeStamp;
        await AuditAsync(false, "CUSTOMERS", LogActionType.LogCustomerRegistered, CoreResources.LogCustomerRegistered, customerId);
        await AuditAsync(false, "CARDS", LogActionType.LogCardIssued, CoreResources.LogCardIssued, card.Id);
        return OperationResult<LoyaltyCard>.Success(card);
    }

    // Issues an additional active card for an active customer (Manager only).
    public Task<OperationResult<LoyaltyCard>> IssueCardAsync(
        int customerId, CancellationToken cancellationToken = default) =>
        ExecuteAsync<OperationResult<LoyaltyCard>>(UserRole.Manager, nameof(IssueCardAsync), "CUSTOMERS", customerId,
            OperationResult<LoyaltyCard>.Failure, async () =>
            {
                if (customerId <= 0) return Invalid<LoyaltyCard>();
                LoyaltyCard card;
                await using (SqliteConnection connection = _connectionFactory.OpenConnection())
                await using (SqliteTransaction transaction = connection.BeginTransaction())
                {
                    Customer? customer = await _customers.GetByIdAsync(customerId, cancellationToken);
                    if (customer is null) return NotFound<LoyaltyCard>();
                    if (!customer.IsActive) return Inactive<LoyaltyCard>();
                    card = new LoyaltyCard(customerId);
                    int id = await _cards.AddAsync(card, transaction, cancellationToken);
                    if (id <= 0) throw new InvalidOperationException();
                    card.Id = id;
                    cancellationToken.ThrowIfCancellationRequested();
                    await transaction.CommitAsync(cancellationToken);
                }
                await AuditAsync(false, "CARDS", LogActionType.LogCardIssued, CoreResources.LogCardIssued, card.Id);
                return OperationResult<LoyaltyCard>.Success(card);
            }, cancellationToken);

    // Basic is the current seeded name; General is the newer documented name.
    private async Task<CustomerCategory?> FindInitialCategoryAsync(CancellationToken token)
    {
        IReadOnlyList<CustomerCategory> categories = await _categories.GetAllAsync(token);
        return categories.FirstOrDefault(c => c.CategoryName.Equals("Basic", StringComparison.OrdinalIgnoreCase))
            ?? categories.FirstOrDefault(c => c.CategoryName.Equals("General", StringComparison.OrdinalIgnoreCase));
    }

    // ==========================================
    // MANAGER-ONLY CHANGES
    // ==========================================

    // Updates personal details only; balance, category, status and registration date are preserved.
    public Task<OperationResult> UpdateCustomerAsync(
        Customer customer, CancellationToken cancellationToken = default) =>
        ChangeAsync(nameof(UpdateCustomerAsync), "CUSTOMERS", customer?.Id, async () =>
        {
            if (customer is null || customer.Id <= 0 || !ValidBirthDate(customer.BirthDate)) return Invalid();
            return await EditCustomerAsync(customer.Id, saved =>
            {
                saved.CustomerName = customer.CustomerName;
                saved.BirthDate = customer.BirthDate;
            }, LogActionType.LogCustomerUpdated, CoreResources.LogCustomerUpdated, cancellationToken);
        }, cancellationToken);

    // Changes only the loyalty category of an existing customer.
    public Task<OperationResult> ChangeCategoryAsync(
        int customerId, int categoryId, CancellationToken cancellationToken = default) =>
        ChangeAsync(nameof(ChangeCategoryAsync), "CUSTOMERS", customerId, async () =>
        {
            if (customerId <= 0 || categoryId <= 0) return Invalid();
            if (await _categories.GetByIdAsync(categoryId, cancellationToken) is null)
                return OperationResult.Failure(CoreResources.ErrorCustomerCategoryMissing, "CATEGORY_NOT_FOUND");
            return await EditCustomerAsync(customerId, saved => saved.CategoryId = categoryId,
                LogActionType.LogCustomerCategoryChanged, CoreResources.LogCustomerCategoryChanged, cancellationToken);
        }, cancellationToken);

    // Acquires a write transaction before reading so another writer cannot change the balance meanwhile.
    private async Task<OperationResult> EditCustomerAsync(
        int customerId, Action<Customer> edit, LogActionType action, string description, CancellationToken token)
    {
        await using (SqliteConnection connection = _connectionFactory.OpenConnection())
        await using (SqliteTransaction transaction = connection.BeginTransaction())
        {
            // The repository reads the committed row on a separate connection under the write reservation.
            Customer? saved = await _customers.GetByIdAsync(customerId, token);
            if (saved is null) return NotFound();
            edit(saved);
            if (!await _customers.UpdateAsync(saved, transaction, token)) return NotFound();
            token.ThrowIfCancellationRequested();
            await transaction.CommitAsync(token);
        }
        await AuditAsync(false, "CUSTOMERS", action, description, customerId);
        return OperationResult.Success();
    }

    // Changes customer status without deleting history or changing individual card statuses.
    public Task<OperationResult> SetCustomerActiveStatusAsync(
        int customerId, bool isActive, CancellationToken cancellationToken = default) =>
        ChangeAsync(nameof(SetCustomerActiveStatusAsync), "CUSTOMERS", customerId, async () =>
        {
            if (customerId <= 0) return Invalid();
            if (!await _customers.SetActiveStatusAsync(customerId, isActive, cancellationToken)) return NotFound();
            await AuditAsync(false, "CUSTOMERS", isActive ? LogActionType.LogCustomerActivated : LogActionType.LogCustomerDeactivated,
                isActive ? CoreResources.LogCustomerActivated : CoreResources.LogCustomerDeactivated, customerId);
            return OperationResult.Success();
        }, cancellationToken);

    // Blocks or unblocks a card independently of its owner's status.
    public Task<OperationResult> SetCardActiveStatusAsync(
        int cardId, bool isActive, CancellationToken cancellationToken = default) =>
        ChangeAsync(nameof(SetCardActiveStatusAsync), "CARDS", cardId, async () =>
        {
            if (cardId <= 0) return Invalid();
            if (!await _cards.SetActiveStatusAsync(cardId, isActive, cancellationToken)) return NotFound();
            await AuditAsync(false, "CARDS", isActive ? LogActionType.LogCardActivated : LogActionType.LogCardBlocked,
                isActive ? CoreResources.LogCardActivated : CoreResources.LogCardBlocked, cardId);
            return OperationResult.Success();
        }, cancellationToken);

    // ==========================================
    // VALIDATION, PERMISSIONS AND LOGGING
    // ==========================================

    private Task<OperationResult<T>> ReadAsync<T>(string operation, Func<Task<OperationResult<T>>> work,
        CancellationToken token, string objectType = "CUSTOMERS", int? objectId = null) =>
        ExecuteAsync<OperationResult<T>>(UserRole.Cashier, operation, objectType, objectId, OperationResult<T>.Failure, work, token);

    private Task<OperationResult> ChangeAsync(string operation, string objectType, int? objectId,
        Func<Task<OperationResult>> work, CancellationToken token) =>
        ExecuteAsync<OperationResult>(UserRole.Manager, operation, objectType, objectId, OperationResult.Failure, work, token);

    // Applies permission checks before any repository access and returns safe structured failures.
    private async Task<TResult> ExecuteAsync<TResult>(UserRole role, string operation, string objectType, int? objectId,
        Func<string, string, TResult> failure, Func<Task<TResult>> work, CancellationToken token)
        where TResult : OperationResult
    {
        token.ThrowIfCancellationRequested();
        TResult result;
        try
        {
            result = _auth.HasPermission(role)
                ? await work()
                : failure(CoreResources.ErrorCustomerAccessDenied, "ACCESS_DENIED");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            result = failure(CoreResources.ErrorCustomerOperationFailed, "CUSTOMER_OPERATION_FAILED");
        }
        if (result.IsFailure)
        {
            bool denied = result.ErrorCode == "ACCESS_DENIED";
            string description = string.Format(denied ? CoreResources.LogCustomerAccessDenied : CoreResources.LogCustomerOperationFailed, operation);
            await AuditAsync(true, objectType, denied ? LogActionType.ErrorCustomerAccessDenied : LogActionType.ErrorCustomerOperation, description, objectId);
        }
        return result;
    }

    // Audit failures must not turn a committed database change into an apparent failed operation.
    private async Task AuditAsync(bool error, string objectType, LogActionType action, string description, int? objectId)
    {
        try
        {
            int? safeId = objectId is > 0 ? objectId : null;
            OperationResult result = error
                ? await _logs.LogErrorAsync(objectType, action, description, safeId, CancellationToken.None)
                : await _logs.LogInfoAsync(objectType, action, description, safeId, CancellationToken.None);
            if (result.IsFailure) Trace.TraceWarning(CoreResources.ErrorCustomerAuditFailed);
        }
        catch (Exception)
        {
            Trace.TraceWarning(CoreResources.ErrorCustomerAuditFailed);
        }
    }

    private static bool ValidBirthDate(DateOnly? birthDate) =>
        birthDate is null || birthDate.Value <= DateOnly.FromDateTime(DateTime.Today);

    private static OperationResult Invalid() => OperationResult.Failure(CoreResources.ErrorCustomerInvalidInput, "INVALID_INPUT");
    private static OperationResult<T> Invalid<T>() => OperationResult<T>.Failure(CoreResources.ErrorCustomerInvalidInput, "INVALID_INPUT");
    private static OperationResult NotFound() => OperationResult.Failure(CoreResources.ErrorCustomerNotFound, "NOT_FOUND");
    private static OperationResult<T> NotFound<T>() => OperationResult<T>.Failure(CoreResources.ErrorCustomerNotFound, "NOT_FOUND");
    private static OperationResult<T> MissingCategory<T>() => OperationResult<T>.Failure(CoreResources.ErrorCustomerCategoryMissing, "CATEGORY_NOT_FOUND");
    private static OperationResult<T> Inactive<T>() => OperationResult<T>.Failure(CoreResources.ErrorCustomerInactive, "CUSTOMER_INACTIVE");
}
