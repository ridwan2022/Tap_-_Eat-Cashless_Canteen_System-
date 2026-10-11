namespace TapAndEat.Api.Models;

/// <summary>Task 13.1 — Customer meal satisfaction rating and qualitative feedback.</summary>
public class OrderFeedback
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int Rating { get; set; } // 1 to 5 stars
    public string Comment { get; set; } = string.Empty;
    public Guid? MenuItemId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
