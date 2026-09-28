namespace TapAndEat.Api.Models;

public enum StockStatus
{
    Available,
    Low,
    Out
}

public class MenuItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Category { get; set; } = "General";
    public List<string> DietaryTags { get; set; } = new();
    public int StockCount { get; set; }

    /// <summary>Low-stock threshold used to compute <see cref="StockStatus"/>.</summary>
    public int LowStockThreshold { get; set; } = 5;

    public bool IsPublished { get; set; }

    /// <summary>
    /// True when this item is today's admin override of the standard menu
    /// (Task 2.3 / 2.4) rather than a regular catalog item.
    /// </summary>
    public bool IsOverride { get; set; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public StockStatus GetStockStatus() => StockCount <= 0
        ? StockStatus.Out
        : StockCount <= LowStockThreshold
            ? StockStatus.Low
            : StockStatus.Available;
}
