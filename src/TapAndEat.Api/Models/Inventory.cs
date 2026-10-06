namespace TapAndEat.Api.Models;

/// <summary>Task 10.1 — one raw ingredient the kitchen tracks stock for (e.g. "Chicken", "Rice").</summary>
public class Ingredient
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = "g"; // free-text unit label (g, ml, pcs, ...)
    public decimal StockQuantity { get; set; }
    public decimal ReorderThreshold { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public bool IsLowStock => StockQuantity <= ReorderThreshold;
}

/// <summary>A menu item's recipe line: how much of one ingredient one portion consumes.</summary>
public class MenuItemIngredient
{
    public Guid MenuItemId { get; init; }
    public Guid IngredientId { get; init; }
    public decimal QuantityPerItem { get; set; }
}

public enum InventoryTransactionReason
{
    OrderDeduction,
    ManualRestock,
    ManualAdjustment
}

/// <summary>Task 10.1/10.2 — an immutable audit entry for every ingredient stock change, so usage is always recoverable/explainable.</summary>
public class InventoryTransaction
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid IngredientId { get; init; }

    /// <summary>Signed: negative for consumption, positive for restocks.</summary>
    public decimal ChangeAmount { get; init; }
    public decimal BalanceAfter { get; init; }
    public InventoryTransactionReason Reason { get; init; }
    public Guid? ReferenceOrderId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
