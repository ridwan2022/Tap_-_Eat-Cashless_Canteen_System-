using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public record DailyDemand(string Date, int Quantity);

public interface IForecastRepository
{
    /// <summary>
    /// Task 9.1 — per-day quantity sold for one menu item from paid/collected
    /// orders, most recent day first, in business time. The historical
    /// record this reads from (order_lines + orders) already exists the
    /// moment an order is paid — nothing extra to capture.
    /// </summary>
    Task<IReadOnlyList<DailyDemand>> GetDailyDemandAsync(Guid menuItemId, int days, double utcOffsetHours, DateTimeOffset asOfUtc);

    Task<DemandForecast?> GetAsync(Guid menuItemId, string forecastDate);
    Task UpsertAsync(DemandForecast forecast);
    Task<IReadOnlyList<DemandForecast>> GetForDateAsync(string forecastDate);
}
