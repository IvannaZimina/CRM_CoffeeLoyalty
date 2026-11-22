using CoffeeLoyalty.Core.DTOs;
using CoffeeLoyalty.Core.Models;
using CoffeeLoyalty.Core.resources;

namespace CoffeeLoyalty.Core.Validation;

// Validates Drink entities before create or update operations.
public static class DrinkValidator
{
    // Checks required fields on Drink and normalizes its relative image path.
    public static OperationResult Validate(Drink? drink, bool requirePositiveId = false)
    {
        if (drink is null)
        {
            return OperationResult.Failure(
                CoreResources.DrinkServiceDrinkNull,
                "INVALID_DRINK");
        }

        if (requirePositiveId && !ValidationHelper.IsPositive(drink.Id))
        {
            return OperationResult.Failure(
                CoreResources.DrinkServiceIdPositive,
                "INVALID_ID");
        }

        if (!ValidationHelper.HasText(drink.DrinkName))
        {
            return OperationResult.Failure(
                CoreResources.DrinkServiceNameRequired,
                "VALIDATION_ERROR");
        }

        if (!ValidationHelper.HasText(drink.DrinkType))
        {
            return OperationResult.Failure(
                CoreResources.DrinkServiceTypeRequired,
                "VALIDATION_ERROR");
        }

        if (!ValidationHelper.IsPositive(drink.DrinkPrice))
        {
            return OperationResult.Failure(
                CoreResources.DrinkServicePricePositive,
                "VALIDATION_ERROR");
        }

        drink.DrinkImageLink = ValidationHelper.NormalizeRelativeImagePath(drink.DrinkImageLink);

        return OperationResult.Success();
    }
}