using Microsoft.Extensions.Options;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;

namespace TapAndEat.Api.Services;

public class ForecastOptions
{
    /// <summary>How many trailing days of paid-order history feed the moving average.</summary>
    public int WindowDays { get; set; } = 7;

    /// <summary>Minimum days of history before a forecast is produced at all (Task 9.5 — sparse-data edge case).</summary>
    public int MinSampleDays { get; set; } = 3;
}

/// <summary>
/// Tasks 9.1–9.3 — baseline demand forecasting. The model is a trailing
/// moving average of units sold per day over the configured window (the
/// Sprint 3 plan explicitly allows "moving average / simple regression" as
/// the baseline) — simple, has no training step, and degrades gracefully
/// with little history, which matters more for a lab project than a fancier
/// model that needs tuning. One forecast is cached per item per business day
/// so repeated dashboard loads don't recompute it.
/// </summary>
public class ForecastService : IForecastService
{
    private readonly IForecastRepository _forecasts;
    private readonly IMenuRepository _menu;
    private readonly ForecastOptions _options;
    private readonly double _utcOffsetHours;
    private readonly TimeProvider _clock;

    public ForecastService(IForecastRepository forecasts, IMenuRepository menu, IOptions<ForecastOptions> options, IOptions<BusinessOptions> business, TimeProvider clock)
    {
        _forecasts = forecasts;
        _menu = menu;
        _options = options.Value;
        _utcOffsetHours = business.Value.UtcOffsetHours;
        _clock = clock;
    }

    public async Task<ForecastDto> GetForecastAsync(Guid menuItemId)
    {
        var item = await _menu.GetByIdAsync(menuItemId)
            ?? throw new ForecastException(ErrorKind.NotFound, "Menu item not found.");
        return await BuildAsync(item);
    }

    public async Task<IReadOnlyList<ForecastDto>> GetAllForecastsAsync()
    {
        var items = (await _menu.GetAllAsync()).Where(i => i.IsPublished).OrderBy(i => i.Name).ToList();
        var result = new List<ForecastDto>();
        foreach (var item in items) result.Add(await BuildAsync(item));
        return result;
    }

    private async Task<ForecastDto> BuildAsync(MenuItem item)
    {
        var now = _clock.GetUtcNow();
        var forDate = BusinessTime.ToLocal(now, _utcOffsetHours).AddDays(1).ToString("yyyy-MM-dd"); // "tomorrow"
        // Recomputed on every call (it's a cheap average) and then upserted, so demand_forecasts
        // ends up holding a running history of predictions actually shown, not just a cache.
        var history = await _forecasts.GetDailyDemandAsync(item.Id, _options.WindowDays, _utcOffsetHours, now);

        // "Sample days" = days in the window with an actual recorded sale for this item — the
        // gate for whether we trust the average at all. Once that bar is cleared, the average
        // itself is taken over the FULL window including zero-sale days, because a quiet day
        // among an otherwise-tracked item is real signal, not missing data.
        var sampleDays = history.Count(d => d.Quantity > 0);
        var predicted = sampleDays >= _options.MinSampleDays
            ? Math.Round((decimal)history.Average(d => d.Quantity), 1)
            : 0m; // Task 9.5 — not enough history yet: report 0 rather than a misleadingly precise guess

        var forecast = new DemandForecast
        {
            MenuItemId = item.Id,
            ForecastDate = forDate,
            PredictedQuantity = predicted,
            SampleDays = sampleDays,
            GeneratedAtUtc = now
        };
        await _forecasts.UpsertAsync(forecast);

        return new ForecastDto(item.Id, item.Name, forDate, predicted, forecast.Model, sampleDays, now,
            history.OrderBy(d => d.Date).Select(d => new DailyActualDto(d.Date, d.Quantity)).ToList());
    }
}
