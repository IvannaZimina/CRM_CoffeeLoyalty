namespace CoffeeLoyalty.Core.DTOs;

/// <summary>
/// Root payload contract for JSON export and import workflows.
/// </summary>
public class DataTransferPayloadDto
{
    /// <summary>
    /// Format version of the exported payload.
    /// </summary>
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// UTC timestamp when the payload was exported.
    /// </summary>
    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Exported or imported drink menu items.
    /// </summary>
    public List<DrinkTransferDto> Drinks { get; set; } = [];

    /// <summary>
    /// Exported or imported customer profiles.
    /// </summary>
    public List<CustomerTransferDto> Customers { get; set; } = [];

    /// <summary>
    /// Exported or imported customer loyalty cards.
    /// </summary>
    public List<CardTransferDto> Cards { get; set; } = [];

    /// <summary>
    /// Exported purchase headers.
    /// </summary>
    public List<PurchaseTransferDto> Purchases { get; set; } = [];

    /// <summary>
    /// Exported audit and error log entries.
    /// </summary>
    public List<LogTransferDto> Logs { get; set; } = [];
}

/// <summary>
/// Serializable representation of a drink menu item (<see cref="Models.Drink"/>).
/// </summary>
public class DrinkTransferDto
{
    public int? Id { get; set; }
    public string? DrinkName { get; set; }
    public decimal? DrinkPrice { get; set; }
    public string? DrinkType { get; set; }
    public bool? IsActive { get; set; }
    public string? DrinkImageLink { get; set; }
}

/// <summary>
/// Serializable representation of a customer (<see cref="Models.Customer"/>).
/// </summary>
public class CustomerTransferDto
{
    public int? Id { get; set; }
    public int? CategoryId { get; set; }
    public string? CustomerName { get; set; }
    public int? BonusBalance { get; set; }
    public DateOnly? BirthDate { get; set; }
    public bool? IsActive { get; set; }
    public DateTime? TimeStamp { get; set; }
}

/// <summary>
/// Serializable representation of a loyalty card (<see cref="Models.LoyaltyCard"/>).
/// </summary>
public class CardTransferDto
{
    public int? Id { get; set; }
    public int? CustomerId { get; set; }
    public bool? IsActive { get; set; }
}

/// <summary>
/// Serializable representation of a purchase header (<see cref="Models.Purchase"/>).
/// </summary>
public class PurchaseTransferDto
{
    public int? Id { get; set; }
    public int? CardId { get; set; }
    public int? EmployeeId { get; set; }
    public decimal? TotalAmount { get; set; }
    public int? BonusesSpent { get; set; }
    public int? BonusesEarned { get; set; }
    public DateTime? TimeStamp { get; set; }
}

/// <summary>
/// Serializable representation of a log entry (<see cref="Models.LogEntry"/>).
/// </summary>
public class LogTransferDto
{
    public int? Id { get; set; }
    public int? ObjectId { get; set; }
    public string? ObjectType { get; set; }
    public string? ActionType { get; set; }
    public string? Description { get; set; }
    public DateTime? TimeStamp { get; set; }
}