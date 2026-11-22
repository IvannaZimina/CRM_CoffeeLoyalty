using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Models;

namespace CoffeeLoyalty.Core.Interfaces.Services;

// Defines business rules for calculating earned bonuses, validating bonus redemption, and computing payable totals.
public interface IBonusService
{
    // Calculates how many bonus points a purchase earns based on the payable amount and customer loyalty category.
    int CalculateEarnedBonuses(
        decimal payableAmount,
        CustomerCategory? category);

    // Checks whether a customer and loyalty card are eligible to earn or redeem bonuses.
    OperationResult CanUseBonuses(
        Customer? customer,
        LoyaltyCard? card);

    // Validates whether the requested number of bonuses can be spent on an order of the given amount.
    OperationResult ValidateBonusRedemption(
        Customer? customer,
        LoyaltyCard? card,
        int bonusesToSpend,
        decimal orderAmount);

    // Calculates the maximum number of bonuses that can be redeemed for the specified order amount and card balance.
    int CalculateMaxRedeemableBonuses(
        decimal orderAmount,
        int availableBonuses);

    // Calculates the final monetary amount payable after applying spent bonuses.
    decimal CalculateFinalAmount(
        decimal orderAmount,
        int bonusesToSpend);

    // Calculates the updated bonus balance after spending and earning bonuses.
    int CalculateUpdatedBalance(
        int currentBalance,
        int bonusesSpent,
        int bonusesEarned);
}