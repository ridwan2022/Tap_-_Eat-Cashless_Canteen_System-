using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

/// <summary>Test double — computes daily demand from an injected order list instead of SQL, so forecast tests don't need a database.</summary>
public class InMemoryForecastRepository : IForecastRepository
{
    private readonly IOrderRepository _orders;
    private readonly Dictionary<(Guid, string), DemandForecast> _forecasts = new();

    public InMemoryForecastRepository(IOrderRepository orders) { _orders = orders; }

    public async Task<IReadOnlyList<DailyDemand>> GetDailyDemandAsync(Guid menuItemId, int days, double utcOffsetHours, DateTimeOffset asOfUtc)
    {
        var paid = (await _orders.GetByStatusAsync(Models.OrderStatus.Paid)).Concat(await _orders.GetByStatusAsync(Models.OrderStatus.Collected));
        var byDay = paid
            .Where(o => o.PaidAtUtc is not null)
            .SelectMany(o => o.Lines.Where(l => l.MenuItemId == menuItemId).Select(l => (Day: BusinessTime.DayKey(o.PaidAtUtc!.Value, utcOffsetHours), l.Quantity)))
            .GroupBy(x => x.Day)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        var today = BusinessTime.DayKey(asOfUtc, utcOffsetHours);
        var result = new List<DailyDemand>();
        for (var i = 1; i <= days; i++)
        {
            var day = BusinessTime.ToLocal(asOfUtc, utcOffsetHours).AddDays(-i).ToString("yyyy-MM-dd");
            if (day == today) continue;
            result.Add(new DailyDemand(day, byDay.GetValueOrDefault(day, 0)));
        }
        return result;
    }

    public Task<DemandForecast?> GetAsync(Guid menuItemId, string forecastDate) =>
        Task.FromResult(_forecasts.GetValueOrDefault((menuItemId, forecastDate)));

    public Task UpsertAsync(DemandForecast forecast) { _forecasts[(forecast.MenuItemId, forecast.ForecastDate)] = forecast; return Task.CompletedTask; }

    public Task<IReadOnlyList<DemandForecast>> GetForDateAsync(string forecastDate)
    {
        IReadOnlyList<DemandForecast> rows = _forecasts.Values.Where(f => f.ForecastDate == forecastDate).ToList();
        return Task.FromResult(rows);
    }
}
