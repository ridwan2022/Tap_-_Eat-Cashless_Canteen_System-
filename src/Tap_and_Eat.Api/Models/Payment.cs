namespace TapAndEat.Api.Models;

public enum PaymentMethod
{
    bKash,
    Nagad,
    Wallet
}

public enum PaymentPurpose
{
    Order,
    WalletTopUp
}

public enum PaymentStatus
{
    Initiated,
    Succeeded,
    Failed,
    Cancelled
}

/// <summary>
/// Tasks 4.1–4.3 — one attempt to pay for an order (or to top up a wallet).
/// (UserId, IdempotencyKey) is unique, which is what makes a retried request
/// return the original attempt instead of charging twice.
/// </summary>
public class Payment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public PaymentPurpose Purpose { get; init; }
    public Guid UserId { get; init; }
    public Guid? OrderId { get; init; }
    public decimal Amount { get; init; }
    public PaymentMethod Method { get; init; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Initiated;
    public string? IdempotencyKey { get; init; }

    /// <summary>The gateway's own id for this payment (bKash paymentID, Nagad paymentReferenceId, ...).</summary>
    public string? GatewayPaymentRef { get; set; }
    public string? GatewayTransactionId { get; set; }
    public string? RedirectUrl { get; set; }
    public string? FailureReason { get; set; }

    /// <summary>True when the money arrived after the order could no longer be paid and was credited to the wallet instead.</summary>
    public bool RefundedToWallet { get; set; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
