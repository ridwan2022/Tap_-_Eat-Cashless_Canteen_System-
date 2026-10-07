namespace TapAndEat.Api.Models;

/// <summary>Task 11.4 — Web push notification subscription record.</summary>
public class PushSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string? P256dh { get; set; }
    public string? Auth { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
