using Microsoft.Extensions.Options;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Services;

namespace TapAndEat.Api.Jobs;

/// <summary>
/// Task 5.4 — the scheduler. Every poll interval it (1) credits any subsidy
/// that is due for the current period and (2) expires unpaid orders so their
/// reserved stock goes back on the menu. Both operations are idempotent, so
/// the interval only affects how promptly they happen.
/// </summary>
public class ScheduledJobsService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ScheduledJobsService> _logger;
    private readonly JobsOptions _options;

    public ScheduledJobsService(IServiceScopeFactory scopes, IOptions<JobsOptions> options, ILogger<ScheduledJobsService> logger)
    {
        _scopes = scopes;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Scheduled jobs are disabled (Jobs:Enabled=false).");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.PollIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var subsidy = await scope.ServiceProvider.GetRequiredService<ISubsidyService>().RunDueCreditsAsync();
                var expired = await scope.ServiceProvider.GetRequiredService<IOrderService>().ExpireStaleOrdersAsync();

                if (subsidy.Credited > 0 || expired > 0)
                {
                    _logger.LogInformation("Scheduled jobs: {Credited} subsidy credit(s) applied, {Expired} order(s) expired.",
                        subsidy.Credited, expired);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Scheduled jobs run failed; will retry next interval.");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
