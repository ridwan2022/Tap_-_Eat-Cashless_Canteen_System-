namespace TapAndEat.Api.Models;

public enum TapOutcome
{
    Approved,
    Rejected
}

public enum TapReason
{
    None,
    UnknownCard,
    NoPaidOrder,
    OrderNotReady,
    AlreadyCollected,
    InvalidToken,
    NoPendingOrder,
    PaymentFailed
}

/// <summary>
/// Task 6.1 — every RFID tap (or manual token lookup) at the counter is
/// recorded with the order/token it matched and the verification result,
/// including rejections, so the counter has an audit trail.
/// </summary>
public class RfidTapEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Source { get; init; } = "Rfid";   // Rfid | Manual | TapToPay
    public string CardUid { get; init; } = string.Empty;
    public DateTimeOffset TappedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public TapOutcome Outcome { get; init; }
    public TapReason Reason { get; init; }
    public string Message { get; init; } = string.Empty;
    public Guid? UserId { get; init; }
    public Guid? OrderId { get; init; }
    public Guid? QueueTokenId { get; init; }
    public int? TokenNumber { get; init; }
    public Guid StaffUserId { get; init; }
}
