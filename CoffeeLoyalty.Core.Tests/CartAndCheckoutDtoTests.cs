using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Models;
using Xunit;

namespace CoffeeLoyalty.Core.Tests;

// Unit tests for cart and checkout DTOs/models (CartItem, PurchaseRequest, PurchaseResult).
public class CartAndCheckoutDtoTests
{
    // Verifies subtotal calculation (DrinkPrice * Quantity) and constructor guard validation on CartItem.
    [Fact]
    public void CartItem_CalculatesSubtotal_AndValidatesInputs()
    {
        Drink latte = new("Latte", 4.25m, "Coffee", true, null) { Id = 1 };
        CartItem item = new(latte, 3);

        // Check line subtotal calculation: 4.25 * 3 = 12.75.
        Assert.Equal(12.75m, item.Subtotal);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(latte, item.Drink);

        // Check null drink and non-positive quantity validation.
        Assert.Throws<ArgumentNullException>(() => new CartItem(null!, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CartItem(latte, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CartItem(latte, -2));
    }

    // Verifies walk-in and loyalty-card PurchaseRequest initialization as well as input validation.
    [Fact]
    public void PurchaseRequest_SupportsWalkInAndLoyaltyCard_AndValidatesFields()
    {
        Drink espresso = new("Espresso", 2.50m, "Coffee", true, null) { Id = 2 };
        CartItem[] items = [new(espresso, 2)];

        // Check walk-in request defaults: cardId is null and bonusesToSpend is 0.
        PurchaseRequest walkIn = new(employeeId: 5, items: items);
        Assert.Equal(5, walkIn.EmployeeId);
        Assert.Null(walkIn.CardId);
        Assert.Equal(0, walkIn.BonusesToSpend);
        Assert.Single(walkIn.Items);

        // Check loyalty order request with card ID and bonus redemption amount.
        PurchaseRequest loyaltyOrder = new(employeeId: 5, items: items, bonusesToSpend: 15, cardId: 42);
        Assert.Equal(42, loyaltyOrder.CardId);
        Assert.Equal(15, loyaltyOrder.BonusesToSpend);

        // Check invalid request arguments throw expected exceptions.
        Assert.Throws<ArgumentOutOfRangeException>(() => new PurchaseRequest(0, items));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PurchaseRequest(5, items, bonusesToSpend: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PurchaseRequest(5, items, cardId: 0));
        Assert.Throws<ArgumentNullException>(() => new PurchaseRequest(5, null!));
    }

    // Verifies PurchaseResult factory methods for both successful receipts and failed checkout attempts.
    [Fact]
    public void PurchaseResult_SuccessAndFailure_PopulateExpectedProperties()
    {
        Drink americano = new("Americano", 3.00m, "Coffee", true, null) { Id = 3 };
        CartItem[] items = [new(americano, 1)];

        // Check successful receipt summary fields and trimmed customer name.
        PurchaseResult success = PurchaseResult.Success(
            purchaseId: 101,
            totalAmount: 3.00m,
            bonusesSpent: 5,
            bonusesEarned: 15,
            updatedBonusBalance: 40,
            items: items,
            customerName: "  Anna  ");

        Assert.True(success.IsSuccess);
        Assert.Equal(101, success.PurchaseId);
        Assert.Equal("Anna", success.CustomerName);
        Assert.Equal(3.00m, success.TotalAmount);
        Assert.Equal(5, success.BonusesSpent);
        Assert.Equal(15, success.BonusesEarned);
        Assert.Equal(40, success.UpdatedBonusBalance);
        Assert.Single(success.Items);

        // Check failure result message and error code.
        PurchaseResult failure = PurchaseResult.Failure("Cart is empty.", "EMPTY_CART");
        Assert.False(failure.IsSuccess);
        Assert.Equal("Cart is empty.", failure.ErrorMessage);
        Assert.Equal("EMPTY_CART", failure.ErrorCode);
    }
}