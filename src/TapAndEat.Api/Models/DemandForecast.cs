namespace TapAndEat.Api.Models;

/// <summary>
/// Task 9.2/9.3 — a cached next-day demand prediction for one menu item,
/// produced by a trailing moving-average model over its recent paid order
/// history. Cached (one row per item per forecast date) so re-requesting the
/// same day's forecast is instant and the prediction is itself recoverable
/// from the database rather than only living in memory.
/// </summary>
public class DemandForecast
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid MenuItemId { get; init; }

    /// <summary>The calendar day (business time, yyyy-MM-dd) this prediction is *for*.</summary>
    public string ForecastDate { get; init; } = string.Empty;
    public decimal PredictedQuantity { get; set; }
    public string Model { get; init; } = "MovingAverage";

    /// <summary>How many days of history actually fed the average (fewer than the configured window when history is sparse).</summary>
    public int SampleDays { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
