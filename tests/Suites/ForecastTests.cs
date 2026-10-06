using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Services;
using TapAndEat.Tests.Framework;

namespace TapAndEat.Tests.Suites;

/// <summary>Sprint 3 — Tasks 9.1-9.3, 9.5: demand forecasting baseline, API and edge cases.</summary>
public static class ForecastTests
{
    public static void Register(TestRunner runner)
    {
        const string forecast = "ForecastService (Tasks 9.1-9.3, 9.5)";

        runner.Add(forecast, "With enough history, the forecast is the trailing moving average", async () =>
        {
            var f = new ServiceFixture { ForecastWindowDays = 5, ForecastMinSampleDays = 3 };
            var user = await f.AddUserAsync();
            var item = await f.AddMenuItemAsync();
            var now = f.Clock.GetUtcNow();
            // 5 trailing business days before "today": 10, 10, 10, 10, 10 units sold -> average 10
            for (var i = 1; i <= 5; i++)
            {
                await f.AddHistoricalOrderAsync(user, item, 10, now.AddDays(-i));
            }

            var result = await f.Forecast.GetForecastAsync(item.Id);

            Assert.Equal(10m, result.PredictedQuantity);
            Assert.Equal(5, result.SampleDays);
            Assert.Equal(5, result.RecentActuals.Count);
        });

        runner.Add(forecast, "Sparse history (fewer days than MinSampleDays) yields 0, not a misleading guess", async () =>
        {
            var f = new ServiceFixture { ForecastWindowDays = 7, ForecastMinSampleDays = 3 };
            var user = await f.AddUserAsync();
            var item = await f.AddMenuItemAsync();
            await f.AddHistoricalOrderAsync(user, item, 20, f.Clock.GetUtcNow().AddDays(-1)); // only "sold" on 1 of the 7 trailing days

            var result = await f.Forecast.GetForecastAsync(item.Id);

            Assert.Equal(0m, result.PredictedQuantity);
            Assert.Equal(1, result.SampleDays); // only 1 of the 7 trailing days actually had a sale
        });

        runner.Add(forecast, "A brand-new item with zero history still returns a (zero) forecast, not an error", async () =>
        {
            var f = new ServiceFixture();
            var item = await f.AddMenuItemAsync();
            var result = await f.Forecast.GetForecastAsync(item.Id);
            Assert.Equal(0m, result.PredictedQuantity);
        });

        runner.Add(forecast, "Today's own (still-open) sales never leak into tomorrow's forecast", async () =>
        {
            var f = new ServiceFixture { ForecastMinSampleDays = 1 };
            var user = await f.AddUserAsync();
            var item = await f.AddMenuItemAsync();
            await f.AddHistoricalOrderAsync(user, item, 999, f.Clock.GetUtcNow()); // "sold" earlier today

            var result = await f.Forecast.GetForecastAsync(item.Id);

            Assert.True(result.RecentActuals.All(a => a.Quantity == 0), "today's sales must not appear in the trailing window");
            Assert.Equal(0m, result.PredictedQuantity);
        });

        runner.Add(forecast, "An unknown menu item is rejected", async () =>
        {
            var f = new ServiceFixture();
            await Assert.ThrowsAsync<ForecastException>(() => f.Forecast.GetForecastAsync(Guid.NewGuid()));
        });
    }
}
