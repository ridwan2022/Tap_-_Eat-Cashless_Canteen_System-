namespace TapAndEat.Api.Models;

/// <summary>
/// Task 5.1 — wallet account: balance plus the (at most one) RFID card linked
/// to it. A card belongs to exactly one wallet; the repository enforces that
/// atomically (the in-memory stand-in for a unique index).
/// </summary>
public class Wallet
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public decimal Balance { get; set; }
    public string? RfidCardUid { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public enum WalletTransactionType
{
    TopUp,
    SubsidyCredit,
    Purchase,
    Refund
}

public class WalletTransaction
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid WalletId { get; init; }
    public WalletTransactionType Type { get; init; }

    /// <summary>Signed: positive for credits, negative for debits.</summary>
    public decimal Amount { get; init; }
    public decimal BalanceAfter { get; init; }
    public string Description { get; init; } = string.Empty;

    /// <summary>Globally unique. Replaying the same key never posts a second transaction.</summary>
    public string IdempotencyKey { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public enum SubsidyFrequency
{
    Daily,
    Weekly,
    Monthly
}

/// <summary>Task 5.1 — enterprise subsidy credit schedule for one employee.</summary>
public class SubsidySchedule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public decimal Amount { get; set; }
    public SubsidyFrequency Frequency { get; set; } = SubsidyFrequency.Monthly;
    public bool IsActive { get; set; } = true;

    /// <summary>Optimisation only — the idempotency key on the wallet transaction is the real double-credit guard.</summary>
    public string? LastCreditedPeriodKey { get; set; }
}
