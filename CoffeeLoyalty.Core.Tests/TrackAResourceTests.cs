
using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Enums;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.resources;
using Xunit;

namespace CoffeeLoyalty.Core.Tests;

public class TrackAResourceTests
{
    [Fact]
    public void InvalidDomainValues_UseResourceMessages()
    {
        var categoryError = Assert.Throws<ArgumentException>(
            () => new CustomerCategory(" "));

        Assert.Contains(
            CoreResources.ValCategoryNameRequired,
            categoryError.Message);

        var customerError = Assert.Throws<ArgumentException>(
            () => new Customer(1, " "));

        Assert.Contains(
            CoreResources.ValCustomerNameRequired,
            customerError.Message);

        var cardError = Assert.Throws<ArgumentOutOfRangeException>(
            () => new LoyaltyCard(0));

        Assert.Contains(
            CoreResources.ValCardCustomerIdPositive,
            cardError.Message);

        var employeeError = Assert.Throws<ArgumentException>(
            () => new Employee(" ", UserRole.Cashier));

        Assert.Contains(
            CoreResources.ValEmployeeNameRequired,
            employeeError.Message);

        var logError = Assert.Throws<ArgumentException>(
            () => new LogEntry(" ", "Created", "Test"));

        Assert.Contains(
            CoreResources.ValLogObjectTypeRequired,
            logError.Message);

        var resultError = Assert.Throws<ArgumentException>(
            () => OperationResult.Failure(""));

        Assert.Contains(
            CoreResources.ErrorResultFailureMessageRequired,
            resultError.Message);
    }
}
