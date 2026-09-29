namespace TapAndEat.Api.Models;

public enum OrderStatus
{
    PendingPayment,
    Paid,
    Collected,
    Cancelled,
    Expired
}

/// <summary>Snapshot of a menu item at order time, so later price/menu edits never change a placed order.</summary>
public class OrderLine
{
    public Guid MenuItemId { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
    public int PrepTimeMinutes { get; init; }
    public decimal LineTotal => UnitPrice * Quantity;
}

/// <summary>
/// Sprint 2 — the order is the object payments, queue tokens and RFID pickup
/// all hang off (Sprint 1 only had a browsable menu). Stock is reserved when
/// the order is created and released again if it is cancelled or expires
/// unpaid.
/// </summary>
public class Order
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public List<OrderLine> Lines { get; init; } = new();
    public decimal Total => Lines.Sum(l => l.LineTotal);
    public OrderStatus Status { get; set; } = OrderStatus.PendingPayment;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public DateTimeOffset? PaidAtUtc { get; set; }
    public DateTimeOffset? CollectedAtUtc { get; set; }

    /// <summary>Task 4.4 — verifiable digital token, only ever set after payment is confirmed.</summary>
    public string? PaymentToken { get; set; }
    public Guid? PaidByPaymentId { get; set; }
}
