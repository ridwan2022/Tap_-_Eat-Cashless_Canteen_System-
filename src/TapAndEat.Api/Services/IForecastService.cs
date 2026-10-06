using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;

namespace TapAndEat.Api.Services;

public class ForecastException : AppException
{
    public ForecastException(ErrorKind kind, string message) : base(kind, message) { }
}

public interface IForecastService
{
    /// <summary>Task 9.3 — next-day demand prediction for one menu item, computed fresh or served from cache (same business day).</summary>
    Task<ForecastDto> GetForecastAsync(Guid menuItemId);

    /// <summary>Forecasts for every published menu item, for the admin dashboard (Task 9.4).</summary>
    Task<IReadOnlyList<ForecastDto>> GetAllForecastsAsync();
}
