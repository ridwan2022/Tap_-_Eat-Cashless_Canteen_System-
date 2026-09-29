using TapAndEat.Api.DTOs;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Services;

public interface ISubsidyService
{
    /// <summary>Task 5.1 — sets (or replaces) an employee's enterprise subsidy schedule.</summary>
    Task<SubsidyScheduleDto> SetScheduleAsync(Guid userId, decimal amount, SubsidyFrequency frequency, bool isActive);

    /// <summary>
    /// Task 5.4 — credits every active schedule whose current period hasn't been
    /// credited yet. Safe to run as often as you like: running it twice in the
    /// same period never credits a wallet twice.
    /// </summary>
    Task<SubsidyRunResultDto> RunDueCreditsAsync();
}
