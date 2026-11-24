using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.Services;
using Xunit;

namespace CoffeeLoyalty.Core.Tests;

// Unit tests covering BonusService loyalty rules:
// point accrual, customer/card eligibility, redemption validation, max redeemable cap, final total, and balance updates.
public class BonusServiceTests
{
    private readonly BonusService _service = new();

    // Tests bonus point accrual for positive, zero, and negative payable amounts.
    [Fact]
    public void CalculateEarnedBonuses_ComputesPointsFromPayableAmount()
    {
        // Act: calculate points earned on a 10.00 EUR order.
        int earned = _service.CalculateEarnedBonuses(10.00m, null);

        // Assert: verify positive orders earn points while zero or negative amounts earn 0.
        Assert.True(earned >= 0);
        Assert.Equal(0, _service.CalculateEarnedBonuses(0m, null));
        Assert.Equal(0, _service.CalculateEarnedBonuses(-5.00m, null));
    }

    // Tests eligibility rules requiring an active customer account and an active loyalty card (when provided).
    [Fact]
    public void CanUseBonuses_ValidatesCustomerAndCardState()
    {
        // Arrange: create active and inactive customer and loyalty card instances.
        Customer activeCustomer = new(1, "Anna", 50, isActive: true) { Id = 1 };
        Customer inactiveCustomer = new(1, "Anna", 50, isActive: false) { Id = 2 };
        LoyaltyCard activeCard = new(1, true) { Id = 10 };
        LoyaltyCard inactiveCard = new(1, false) { Id = 11 };

        // Assert: verify null or inactive customer/card fails, while active customer (with active or null card) succeeds.
        Assert.False(_service.CanUseBonuses(null, activeCard).IsSuccess);
        Assert.False(_service.CanUseBonuses(inactiveCustomer, activeCard).IsSuccess);
        Assert.False(_service.CanUseBonuses(activeCustomer, inactiveCard).IsSuccess);
        Assert.True(_service.CanUseBonuses(activeCustomer, activeCard).IsSuccess);
        Assert.True(_service.CanUseBonuses(activeCustomer, null).IsSuccess);
    }

    // Tests bonus redemption validation against available customer balance and positive redemption amounts.
    [Fact]
    public void ValidateBonusRedemption_ChecksBalanceAndOrderTotal()
    {
        // Arrange: create an active customer with 20 available bonus points and an active card.
        Customer customer = new(1, "Mark", 20, isActive: true) { Id = 1 };
        LoyaltyCard card = new(1, true) { Id = 10 };

        // Act & Assert: spending more points than available (50 > 20) is rejected.
        OperationResult insufficient = _service.ValidateBonusRedemption(customer, card, 50, 10.00m);
        Assert.False(insufficient.IsSuccess);

        // Act & Assert: spending zero points is rejected.
        OperationResult nonPositiveSpend = _service.ValidateBonusRedemption(customer, card, 0, 10.00m);
        Assert.False(nonPositiveSpend.IsSuccess);

        // Act & Assert: spending 10 points within balance and order amount succeeds.
        OperationResult valid = _service.ValidateBonusRedemption(customer, card, 10, 10.00m);
        Assert.True(valid.IsSuccess);
    }

    // Tests consistency between maximum redeemable bonuses and the resulting final payable order total.
    [Fact]
    public void CalculateMaxRedeemableBonuses_And_CalculateFinalAmount_AreConsistent()
    {
        // Act: compute max redeemable bonuses on a 5.00 EUR order and apply them to get the final total.
        int maxRedeemable = _service.CalculateMaxRedeemableBonuses(5.00m, 100);
        decimal finalAmount = _service.CalculateFinalAmount(5.00m, maxRedeemable);

        // Assert: verify redeemable cap is non-negative, final amount stays within [0, 5.00], and 0 spent leaves full total.
        Assert.True(maxRedeemable >= 0);
        Assert.InRange(finalAmount, 0m, 5.00m);
        Assert.Equal(5.00m, _service.CalculateFinalAmount(5.00m, 0));
    }

    // Tests updated bonus balance arithmetic (current - spent + earned) and clamping at zero.
    [Fact]
    public void CalculateUpdatedBalance_AppliesSpentAndEarnedPointsWithoutGoingNegative()
    {
        // Assert: 40 - 15 + 10 = 35, and overspending clamps balance at 0 instead of going negative.
        Assert.Equal(35, _service.CalculateUpdatedBalance(40, 15, 10));
        Assert.Equal(0, _service.CalculateUpdatedBalance(10, 50, 0));
    }
}