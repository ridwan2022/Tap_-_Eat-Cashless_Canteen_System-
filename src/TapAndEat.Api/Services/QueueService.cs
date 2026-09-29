using Microsoft.Extensions.Options;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;

namespace TapAndEat.Api.Services;

/// <summary>
/// Task 7.2 — how long a new order will take.
///
/// Work already queued is shared by the kitchen's parallel stations, so it
/// delays a new order by ceil(workAhead / stations); then the order itself
/// takes as long as its slowest line. Minimum 1 minute. Pure function so the
/// estimate is easy to unit-test (Task 7.4).
/// </summary>
public static class PrepTimeEstimator
{
    public static int Estimate(int workAheadMinutes, int ownMinutes, int stations)
    {
        stations = Math.Max(1, stations);
        var wait = (int)Math.Ceiling(Math.Max(0, workAheadMinutes) / (double)stations);
        return Math.Max(1, wait + Math.Max(0, ownMinutes));
    }
}

/// <summary>Tasks 7.2, 7.3 — token generation, live prep-time estimates, and the kitchen board.</summary>
public class QueueService : IQueueService
{
    private readonly IQueueTokenRepository _tokens;
    private readonly IOrderRepository _orders;
    private readonly IEventBroadcaster _events;
    private readonly TimeProvider _clock;
    private readonly int _stations;
    private readonly double _utcOffsetHours;

    public QueueService(
        IQueueTokenRepository tokens,
        IOrderRepository orders,
        IEventBroadcaster events,
        IOptions<KitchenOptions> kitchen,
        IOptions<BusinessOptions> business,
        TimeProvider clock)
    {
        _tokens = tokens;
        _orders = orders;
        _events = events;
        _clock = clock;
        _stations = Math.Max(1, kitchen.Value.Stations);
        _utcOffsetHours = business.Value.UtcOffsetHours;
    }

    public async Task<QueueToken> EnsureTokenForOrderAsync(Order order)
    {
        var now = _clock.GetUtcNow();
        var dayKey = BusinessTime.DayKey(now, _utcOffsetHours);

        var active = await _tokens.GetActiveAsync();
        var workAhead = active
            .Where(t => t.Status is QueueTokenStatus.Queued or QueueTokenStatus.Preparing)
            .Sum(t => t.WorkMinutes);

        var work = order.Lines.Sum(l => l.Quantity * l.PrepTimeMinutes);
        var own = order.Lines.Count == 0 ? 0 : order.Lines.Max(l => l.PrepTimeMinutes);
        var estimate = PrepTimeEstimator.Estimate(workAhead, own, _stations);

        var token = await _tokens.AddForOrderAsync(order.Id, dayKey, number => new QueueToken
        {
            OrderId = order.Id,
            DayKey = dayKey,
            TokenNumber = number,
            EstimatedPrepMinutes = estimate,
            WorkMinutes = work,
            OwnMinutes = own,
            CreatedAtUtc = now
        });

        _events.Publish(new ServerEvent("token", new { tokenId = token.Id, token.TokenNumber, status = token.Status.ToString() }));
        return token;
    }

    public async Task<QueueTokenDto?> GetTokenForOrderAsync(Guid orderId)
    {
        var token = await _tokens.GetByOrderIdAsync(orderId);
        if (token is null) return null;
        var active = await _tokens.GetActiveAsync();
        return await ToDtoAsync(token, active);
    }

    public async Task<QueueTokenDto?> GetTokenByNumberAsync(int tokenNumber)
    {
        var dayKey = BusinessTime.DayKey(_clock.GetUtcNow(), _utcOffsetHours);
        var token = await _tokens.GetByNumberAsync(dayKey, tokenNumber);
        if (token is null) return null;
        var active = await _tokens.GetActiveAsync();
        return await ToDtoAsync(token, active);
    }

    public async Task<KitchenBoardDto> GetBoardAsync()
    {
        var active = await _tokens.GetActiveAsync();
        var dtos = new List<QueueTokenDto>();
        foreach (var token in active)
        {
            dtos.Add(await ToDtoAsync(token, active));
        }
        return new KitchenBoardDto(_clock.GetUtcNow(), _stations, dtos);
    }

    public async Task<QueueTokenDto> SetStatusAsync(Guid tokenId, QueueTokenStatus newStatus)
    {
        var token = await _tokens.GetByIdAsync(tokenId)
            ?? throw new QueueException(ErrorKind.NotFound, "Token not found.");

        if (newStatus == QueueTokenStatus.Collected)
        {
            throw new QueueException(ErrorKind.Invalid, "Tokens are marked collected at the counter, when the meal is handed over.");
        }

        if (newStatus != token.Status)
        {
            if (newStatus < token.Status || token.Status == QueueTokenStatus.Collected)
            {
                throw new QueueException(ErrorKind.Conflict,
                    $"Token {token.Label} is already {token.Status}; it can't go back to {newStatus}.");
            }

            var now = _clock.GetUtcNow();
            token.Status = newStatus;
            if (newStatus == QueueTokenStatus.Preparing) token.StartedAtUtc = now;
            if (newStatus == QueueTokenStatus.Ready)
            {
                token.StartedAtUtc ??= now;
                token.ReadyAtUtc = now;
            }
            await _tokens.UpdateAsync(token);
            _events.Publish(new ServerEvent("token", new { tokenId = token.Id, token.TokenNumber, status = token.Status.ToString() }));
        }

        var active = await _tokens.GetActiveAsync();
        return await ToDtoAsync(token, active);
    }

    public async Task MarkCollectedAsync(Guid orderId)
    {
        var token = await _tokens.GetByOrderIdAsync(orderId);
        if (token is null || token.Status == QueueTokenStatus.Collected) return;

        token.Status = QueueTokenStatus.Collected;
        token.CollectedAtUtc = _clock.GetUtcNow();
        await _tokens.UpdateAsync(token);
        _events.Publish(new ServerEvent("token", new { tokenId = token.Id, token.TokenNumber, status = token.Status.ToString() }));
    }

    private async Task<QueueTokenDto> ToDtoAsync(QueueToken token, IReadOnlyList<QueueToken> active)
    {
        var order = await _orders.GetByIdAsync(token.OrderId);
        var items = order?.Lines.Select(l => new OrderLineSummary(l.Name, l.Quantity)).ToList() ?? new List<OrderLineSummary>();
        return new QueueTokenDto(
            token.Id, token.OrderId, token.TokenNumber, token.Label, token.Status.ToString(),
            token.EstimatedPrepMinutes, RemainingMinutes(token, active), token.CreatedAtUtc, items);
    }

    /// <summary>What's left to wait right now, from the live queue (tokens ahead that aren't Ready yet).</summary>
    private int RemainingMinutes(QueueToken token, IReadOnlyList<QueueToken> active)
    {
        if (token.Status is QueueTokenStatus.Ready or QueueTokenStatus.Collected) return 0;

        var workAhead = active
            .Where(t => t.Id != token.Id
                        && t.Status is QueueTokenStatus.Queued or QueueTokenStatus.Preparing
                        && (t.CreatedAtUtc < token.CreatedAtUtc
                            || (t.CreatedAtUtc == token.CreatedAtUtc && t.TokenNumber < token.TokenNumber)))
            .Sum(t => t.WorkMinutes);

        return PrepTimeEstimator.Estimate(workAhead, token.OwnMinutes, _stations);
    }
}
