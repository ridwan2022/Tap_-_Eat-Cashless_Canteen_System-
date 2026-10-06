using TapAndEat.Api.Db;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories.Sql;

public class SqlForecastRepository : IForecastRepository
{
    private readonly Database _db;
    public SqlForecastRepository(Database db) { _db = db; }

    private static DemandForecast Map(DbRow r) => new()
    {
        Id = r.GetGuid("id"),
        MenuItemId = r.GetGuid("menu_item_id"),
        ForecastDate = r.GetString("forecast_date"),
        PredictedQuantity = r.GetDecimal("predicted_quantity"),
        Model = r.GetString("model"),
        SampleDays = r.GetInt32("sample_days"),
        GeneratedAtUtc = r.GetDateTimeOffset("generated_at")
    };

    public async Task<IReadOnlyList<DailyDemand>> GetDailyDemandAsync(Guid menuItemId, int days, double utcOffsetHours, DateTimeOffset asOfUtc)
    {
        // Business-time day key is computed in C# (SQLite has no notion of our UTC+N offset),
        // so pull enough recent paid/collected lines and group them here.
        var cutoff = asOfUtc.AddDays(-days - 1);
        var rows = await _db.QueryAsync(
            """
            SELECT o.paid_at AS paid_at, ol.quantity AS quantity
            FROM order_lines ol
            JOIN orders o ON o.id = ol.order_id
            WHERE ol.menu_item_id = ? AND o.status IN ('Paid','Collected') AND o.paid_at IS NOT NULL AND o.paid_at >= ?
            """,
            r => (PaidAt: r.GetDateTimeOffset("paid_at"), Qty: r.GetInt32("quantity")),
            menuItemId, cutoff);

        var byDay = rows
            .GroupBy(r => BusinessTime.DayKey(r.PaidAt, utcOffsetHours))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

        var today = BusinessTime.DayKey(asOfUtc, utcOffsetHours);
        var result = new List<DailyDemand>();
        for (var i = 1; i <= days; i++) // yesterday back to `days` days ago — forecasting "tomorrow" never uses today's still-open day
        {
            var day = BusinessTime.ToLocal(asOfUtc, utcOffsetHours).AddDays(-i).ToString("yyyy-MM-dd");
            if (day == today) continue;
            result.Add(new DailyDemand(day, byDay.GetValueOrDefault(day, 0)));
        }
        return result;
    }

    public async Task<DemandForecast?> GetAsync(Guid menuItemId, string forecastDate) =>
        await _db.QuerySingleOrDefaultAsync("SELECT * FROM demand_forecasts WHERE menu_item_id = ? AND forecast_date = ?", Map, menuItemId, forecastDate);

    public Task UpsertAsync(DemandForecast forecast) => _db.ExecuteAsync(
        """
        INSERT INTO demand_forecasts (id, menu_item_id, forecast_date, predicted_quantity, model, sample_days, generated_at)
        VALUES (?,?,?,?,?,?,?)
        ON CONFLICT(menu_item_id, forecast_date) DO UPDATE SET
            predicted_quantity = excluded.predicted_quantity, model = excluded.model,
            sample_days = excluded.sample_days, generated_at = excluded.generated_at
        """,
        forecast.Id, forecast.MenuItemId, forecast.ForecastDate, forecast.PredictedQuantity, forecast.Model, forecast.SampleDays, forecast.GeneratedAtUtc);

    public async Task<IReadOnlyList<DemandForecast>> GetForDateAsync(string forecastDate) =>
        await _db.QueryAsync("SELECT * FROM demand_forecasts WHERE forecast_date = ?", Map, forecastDate);
}
