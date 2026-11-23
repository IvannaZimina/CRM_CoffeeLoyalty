using CoffeeLoyalty.Core.Validation;

namespace CoffeeLoyalty.Core.Helpers;

// Contains pure bonus calculation helpers and conversion rates.
// Accrual rate: 1 EUR = 5 bonus points. Redemption discount: 1 bonus point = 0.01 EUR.
public static class BonusCalculationHelper
{
    public const decimal PointsPerEuro = 5m;
    public const decimal EuroDiscountPerBonusPoint = 0.01m;

    // Calculates bonus points earned for a given EUR amount (1 EUR = 5 points).
    public static int CalculateEarnedPoints(decimal payableAmount)
    {
        if (!ValidationHelper.IsPositive(payableAmount))
        {
            return 0;
        }

        // Convert payable EUR amount into integer bonus points without exceeding int.MaxValue.
        decimal rawPoints = decimal.Floor(payableAmount * PointsPerEuro);
        if (rawPoints <= 0m)
        {
            return 0;
        }

        return rawPoints >= int.MaxValue
            ? int.MaxValue
            : (int)rawPoints;
    }

    // Calculates EUR discount corresponding to a number of redeemed bonus points (1 point = 0.01 EUR).
    public static decimal CalculateDiscountAmount(int bonusesToSpend)
    {
        if (!ValidationHelper.IsPositive(bonusesToSpend))
        {
            return 0m;
        }

        return bonusesToSpend * EuroDiscountPerBonusPoint;
    }

    // Calculates maximum bonus points covered by the specified order amount.
    public static int CalculateMaxBonusesForAmount(decimal orderAmount)
    {
        if (!ValidationHelper.IsPositive(orderAmount))
        {
            return 0;
        }

        decimal maxByOrderDecimal = decimal.Floor(orderAmount / EuroDiscountPerBonusPoint);
        return maxByOrderDecimal >= int.MaxValue
            ? int.MaxValue
            : (int)maxByOrderDecimal;
    }
}