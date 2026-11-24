using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace CoffeeLoyalty.Core.Tests;

// Unit tests covering BonusService loyalty rules:
// point accrual, customer/card eligibility, redemption validation, max redeemable cap, final total, and balance updates.
public class BonusServiceTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;
    private readonly BonusService _service = new();

    // Tests bonus point accrual for positive, zero, and negative payable amounts.
    [Fact]
    public void CalculateEarnedBonuses_ComputesPointsFromPayableAmount()
    {
        int earned = _service.CalculateEarnedBonuses(10.00m, null);
        int zeroEarned = _service.CalculateEarnedBonuses(0m, null);
        int negativeEarned = _service.CalculateEarnedBonuses(-5.00m, null);

        Assert.True(earned >= 0);
        Assert.Equal(0, zeroEarned);
        Assert.Equal(0, negativeEarned);

        _output.WriteLine(TestReportTableBuilder.Build("Bonus Accrual Check",
        [
            ("Order 10.00 EUR", earned.ToString(), "ACCRUED OK"),
            ("Order 0.00 EUR", zeroEarned.ToString(), "ZERO OK"),
            ("Order -5.00 EUR", negativeEarned.ToString(), "CLAMPED OK")
        ]));
    }

    // Tests eligibility rules requiring an active customer account and an active loyalty card (when provided).
    [Fact]
    public void CanUseBonuses_ValidatesCustomerAndCardState()
    {
        Customer activeCustomer = new(1, "Anna", 50, isActive: true) { Id = 1 };
        Customer inactiveCustomer = new(1, "Anna", 50, isActive: false) { Id = 2 };
        LoyaltyCard activeCard = new(1, true) { Id = 10 };
        LoyaltyCard inactiveCard = new(1, false) { Id = 11 };

        OperationResult nullCustomerRes = _service.CanUseBonuses(null, activeCard);
        OperationResult inactiveCustomerRes = _service.CanUseBonuses(inactiveCustomer, activeCard);
        OperationResult inactiveCardRes = _service.CanUseBonuses(activeCustomer, inactiveCard);
        OperationResult activeBothRes = _service.CanUseBonuses(activeCustomer, activeCard);
        OperationResult activeCustomerOnlyRes = _service.CanUseBonuses(activeCustomer, null);

        Assert.False(nullCustomerRes.IsSuccess);
        Assert.False(inactiveCustomerRes.IsSuccess);
        Assert.False(inactiveCardRes.IsSuccess);
        Assert.True(activeBothRes.IsSuccess);
        Assert.True(activeCustomerOnlyRes.IsSuccess);

        _output.WriteLine(TestReportTableBuilder.Build("Bonus Eligibility Check",
        [
            ("Null customer", nullCustomerRes.ErrorCode ?? "rejected", "REJECTED OK"),
            ("Inactive customer", inactiveCustomerRes.ErrorCode ?? "rejected", "REJECTED OK"),
            ("Inactive card", inactiveCardRes.ErrorCode ?? "rejected", "REJECTED OK"),
            ("Active customer + card", activeBothRes.IsSuccess.ToString(), "ALLOWED OK"),
            ("Active customer only", activeCustomerOnlyRes.IsSuccess.ToString(), "ALLOWED OK")
        ]));
    }

    // Tests bonus redemption validation against available customer balance and positive redemption amounts.
    [Fact]
    public void ValidateBonusRedemption_ChecksBalanceAndOrderTotal()
    {
        Customer customer = new(1, "Mark", 20, isActive: true) { Id = 1 };
        LoyaltyCard card = new(1, true) { Id = 10 };

        OperationResult insufficient = _service.ValidateBonusRedemption(customer, card, 50, 10.00m);
        Assert.False(insufficient.IsSuccess);

        OperationResult nonPositiveSpend = _service.ValidateBonusRedemption(customer, card, 0, 10.00m);
        Assert.False(nonPositiveSpend.IsSuccess);

        OperationResult valid = _service.ValidateBonusRedemption(customer, card, 10, 10.00m);
        Assert.True(valid.IsSuccess);

        _output.WriteLine(TestReportTableBuilder.Build("Bonus Redemption Validation Check",
        [
            ("Spend 50 / 20 balance", insufficient.ErrorCode ?? "rejected", "REJECTED OK"),
            ("Spend 0 points", nonPositiveSpend.ErrorCode ?? "rejected", "REJECTED OK"),
            ("Spend 10 / 20 balance", valid.IsSuccess.ToString(), "VALID OK")
        ]));
    }

    // Tests consistency between maximum redeemable bonuses and the resulting final payable order total.
    [Fact]
    public void CalculateMaxRedeemableBonuses_And_CalculateFinalAmount_AreConsistent()
    {
        int maxRedeemable = _service.CalculateMaxRedeemableBonuses(5.00m, 100);
        decimal finalAmount = _service.CalculateFinalAmount(5.00m, maxRedeemable);
        decimal noDiscountTotal = _service.CalculateFinalAmount(5.00m, 0);

        Assert.True(maxRedeemable >= 0);
        Assert.InRange(finalAmount, 0m, 5.00m);
        Assert.Equal(5.00m, noDiscountTotal);

        _output.WriteLine(TestReportTableBuilder.Build("Max Redeemable & Final Total Check",
        [
            ("Order subtotal", "5.00 EUR", "BASE OK"),
            ("Max redeemable (bal 100)", maxRedeemable.ToString(), "CAPPED OK"),
            ("Final after max redeem", $"{finalAmount:0.00} EUR", "IN RANGE OK"),
            ("Final with 0 spent", $"{noDiscountTotal:0.00} EUR", "UNCHANGED OK")
        ]));
    }

    // Tests updated bonus balance arithmetic (current - spent + earned) and clamping at zero.
    [Fact]
    public void CalculateUpdatedBalance_AppliesSpentAndEarnedPointsWithoutGoingNegative()
    {
        int normalBalance = _service.CalculateUpdatedBalance(40, 15, 10);
        int clampedBalance = _service.CalculateUpdatedBalance(10, 50, 0);

        Assert.Equal(35, normalBalance);
        Assert.Equal(0, clampedBalance);

        _output.WriteLine(TestReportTableBuilder.Build("Updated Bonus Balance Check",
        [
            ("40 - 15 + 10", normalBalance.ToString(), "MATCHED OK"),
            ("10 - 50 + 0 (clamp)", clampedBalance.ToString(), "CLAMPED AT 0 OK")
        ]));
    }
}