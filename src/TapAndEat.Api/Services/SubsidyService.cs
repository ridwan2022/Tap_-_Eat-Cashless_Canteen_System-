using System.Globalization;
using Microsoft.Extensions.Options;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;

namespace TapAndEat.Api.Services;

/// <summary>
/// Task 5.4 — scheduled enterprise subsidy credits.
///
/// Idempotency has two layers: (1) each schedule remembers the last period it
/// credited, and (2) the credit itself carries the key
/// "subsidy:{user}:{period}", which the wallet ledger refuses to post twice —
/// so even two overlapping runs (or a restart mid-run) can't double-credit.
/// Periods are calendar days/ISO weeks/months in business (Bangladesh) time.
/// </summary>
public class SubsidyService : ISubsidyService
{
    private readonly IWalletRepository _wallets;
    private readonly IWalletService _walletService;
    private readonly IUserRepository _users;
    private readonly TimeProvider _clock;
    private readonly double _utcOffsetHours;

    public SubsidyService(
        IWalletRepository wallets,
        IWalletService walletService,
        IUserRepository users,
        IOptions<BusinessOptions> business,
        TimeProvider clock)
    {
        _wallets = wallets;
        _walletService = walletService;
        _users = users;
        _clock = clock;
        _utcOffsetHours = business.Value.UtcOffsetHours;
    }

    public async Task<SubsidyScheduleDto> SetScheduleAsync(Guid userId, decimal amount, SubsidyFrequency frequency, bool isActive)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
        {
            throw new WalletException(ErrorKind.Invalid, "Subsidy amount must be greater than zero with at most 2 decimal places.");
        }
        if (await _users.GetByIdAsync(userId) is null)
        {
            throw new WalletException(ErrorKind.NotFound, "User not found.");
        }

        var schedule = await _wallets.GetScheduleAsync(userId) ?? new SubsidySchedule { UserId = userId };
        schedule.Amount = amount;
        schedule.Frequency = frequency;
        schedule.IsActive = isActive;
        await _wallets.UpsertScheduleAsync(schedule);
        return WalletService.ToDto(schedule);
    }

    public async Task<SubsidyRunResultDto> RunDueCreditsAsync()
    {
        var local = BusinessTime.ToLocal(_clock.GetUtcNow(), _utcOffsetHours);
        var credited = 0;
        var already = 0;

        foreach (var schedule in await _wallets.GetActiveSchedulesAsync())
        {
            var user = await _users.GetByIdAsync(schedule.UserId);
            if (user is null || !user.IsActive) continue;

            var period = PeriodKey(schedule.Frequency, local);
            if (schedule.LastCreditedPeriodKey == period)
            {
                already++;
                continue;
            }

            var posting = await _walletService.CreditAsync(
                schedule.UserId, schedule.Amount, WalletTransactionType.SubsidyCredit,
                $"Enterprise subsidy ({period})",
                $"subsidy:{schedule.UserId:N}:{period}");

            schedule.LastCreditedPeriodKey = period;
            await _wallets.UpsertScheduleAsync(schedule);

            if (posting.Created) credited++;
            else already++;
        }

        return new SubsidyRunResultDto(credited, already);
    }

    public static string PeriodKey(SubsidyFrequency frequency, DateTimeOffset local) => frequency switch
    {
        SubsidyFrequency.Daily => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        SubsidyFrequency.Weekly => $"{ISOWeek.GetYear(local.DateTime)}-W{ISOWeek.GetWeekOfYear(local.DateTime):00}",
        _ => local.ToString("yyyy-MM", CultureInfo.InvariantCulture)
    };
}
