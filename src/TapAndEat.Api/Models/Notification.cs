namespace TapAndEat.Api.Models;

public static class NotificationType
{
    public const string OrderPaid = "OrderPaid";
    public const string TokenPreparing = "TokenPreparing";
    public const string TokenReady = "TokenReady";
    public const string OrderCollected = "OrderCollected";
    public const string LowStock = "LowStock";
    public const string SubsidyCredited = "SubsidyCredited";
    public const string General = "General";
}

public static class NotificationChannel
{
    public const string InApp = "InApp";
    public const string Push = "Push";
    public const string Both = "Both";
}

/// <summary>Task 11.1 — in-app and push notification record.</summary>
public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Type { get; set; } = NotificationType.General;
    public string Channel { get; set; } = NotificationChannel.InApp;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ReadAtUtc { get; set; }
}
