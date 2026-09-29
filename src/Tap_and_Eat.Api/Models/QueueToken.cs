namespace TapAndEat.Api.Models;

public enum QueueTokenStatus
{
    Queued,
    Preparing,
    Ready,
    Collected
}

/// <summary>
/// Task 7.1 — queue token: a token number that is unique within its service
/// day, an estimated prep time, and a status. Exactly one per paid order.
/// </summary>
public class QueueToken
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid OrderId { get; init; }

    /// <summary>Service day (local date, yyyy-MM-dd). Numbering restarts each day.</summary>
    public string DayKey { get; init; } = string.Empty;
    public int TokenNumber { get; init; }
    public string Label => $"T-{TokenNumber:000}";
    public QueueTokenStatus Status { get; set; } = QueueTokenStatus.Queued;

    /// <summary>Prep-time estimate at the moment the token was issued (minutes).</summary>
    public int EstimatedPrepMinutes { get; init; }

    /// <summary>Total kitchen work this order adds to the queue (sum of quantity x item prep time).</summary>
    public int WorkMinutes { get; init; }

    
    public int OwnMinutes { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? ReadyAtUtc { get; set; }
    public DateTimeOffset? CollectedAtUtc { get; set; }
}