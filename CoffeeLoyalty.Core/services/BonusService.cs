using System.Globalization;
using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Helpers;
using CoffeeLoyalty.Core.Interfaces.Services;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.resources;
using CoffeeLoyalty.Core.Validation;

namespace CoffeeLoyalty.Core.Services;

// Implements loyalty bonus calculations, redemption validation, and balance/payable helpers.
// Delegates rate-based formulas to BonusCalculationHelper.
public class BonusService : IBonusService
{
    // Calculates earned bonus points from the payable amount (1 EUR = 5 points).
    // Loyalty category does not alter the accrual rate at this stage.
    public int CalculateEarnedBonuses(
        decimal payableAmount,
        CustomerCategory? category)
    {
        return BonusCalculationHelper.CalculateEarnedPoints(payableAmount);
    }

    // Checks whether the customer account and optional loyalty card are active and eligible for bonuses.
    public OperationResult CanUseBonuses(
        Customer? customer,
        LoyaltyCard? card)
    {
        if (customer is null)
        {
            return OperationResult.Failure(
                CoreResources.ValRequiredField,
                "CUSTOMER_REQUIRED");
        }

        if (!customer.IsActive)
        {
            return OperationResult.Failure(
                CoreResources.BonusCustomerInactive,
                "CUSTOMER_INACTIVE");
        }

        if (card is not null && !card.IsActive)
        {
            return OperationResult.Failure(
                CoreResources.BonusCardInactive,
                "CARD_INACTIVE");
        }

        return OperationResult.Success();
    }

    // Validates that bonusesToSpend is allowed for the given customer, card, and order amount.
    public OperationResult ValidateBonusRedemption(
        Customer? customer,
        LoyaltyCard? card,
        int bonusesToSpend,
        decimal orderAmount)
    {
        // First ensure customer and card eligibility.
        OperationResult eligibilityResult = CanUseBonuses(customer, card);
        if (!eligibilityResult.IsSuccess)
        {
            return eligibilityResult;
        }

        if (!ValidationHelper.IsNonNegative(orderAmount))
        {
            return OperationResult.Failure(
                string.Format(
                    CultureInfo.InvariantCulture,
                    CoreResources.BonusOrderAmountNonNegative,
                    orderAmount),
                "INVALID_ORDER_AMOUNT");
        }

        if (!ValidationHelper.IsPositive(bonusesToSpend))
        {
            return OperationResult.Failure(
                string.Format(
                    CultureInfo.InvariantCulture,
                    CoreResources.BonusRedeemAmountPositive,
                    bonusesToSpend),
                "INVALID_BONUS_AMOUNT");
        }

        // Ensure customer has enough bonus points available.
        if (customer!.BonusBalance < bonusesToSpend)
        {
            return OperationResult.Failure(
                string.Format(
                    CultureInfo.InvariantCulture,
                    CoreResources.BonusInsufficientBalance,
                    bonusesToSpend,
                    customer.BonusBalance),
                "INSUFFICIENT_BONUSES");
        }

        // Ensure discount does not exceed the order total.
        decimal discountAmount = BonusCalculationHelper.CalculateDiscountAmount(bonusesToSpend);
        if (discountAmount > orderAmount)
        {
            return OperationResult.Failure(
                string.Format(
                    CultureInfo.InvariantCulture,
                    CoreResources.BonusDiscountExceedsOrder,
                    discountAmount,
                    orderAmount),
                "DISCOUNT_EXCEEDS_ORDER");
        }

        return OperationResult.Success();
    }

    // Calculates the maximum bonus points that can be spent on orderAmount given availableBonuses.
    public int CalculateMaxRedeemableBonuses(
        decimal orderAmount,
        int availableBonuses)
    {
        if (!ValidationHelper.IsPositive(orderAmount) || !ValidationHelper.IsPositive(availableBonuses))
        {
            return 0;
        }

        int maxByOrder = BonusCalculationHelper.CalculateMaxBonusesForAmount(orderAmount);
        return Math.Min(availableBonuses, maxByOrder);
    }

    // Calculates the final payable amount after applying bonus discount.
    public decimal CalculateFinalAmount(
        decimal orderAmount,
        int bonusesToSpend)
    {
        if (!ValidationHelper.IsNonNegative(orderAmount))
        {
            return 0m;
        }

        if (!ValidationHelper.IsPositive(bonusesToSpend))
        {
            return orderAmount;
        }

        decimal discount = BonusCalculationHelper.CalculateDiscountAmount(bonusesToSpend);
        decimal finalAmount = orderAmount - discount;

        return finalAmount > 0m ? finalAmount : 0m;
    }

    // Calculates the resulting bonus balance after subtracting spent points and adding earned points.
    public int CalculateUpdatedBalance(
        int currentBalance,
        int bonusesSpent,
        int bonusesEarned)
    {
        long updated = (long)currentBalance - bonusesSpent + bonusesEarned;
        if (updated <= 0)
        {
            return 0;
        }

        return updated >= int.MaxValue
            ? int.MaxValue
            : (int)updated;
    }
}