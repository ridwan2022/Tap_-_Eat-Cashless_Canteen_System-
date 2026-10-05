using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;

namespace TapAndEat.Api.Services;

/// <summary>
/// Task 8.1 — queue position: how many active tokens (Queued/Preparing) are
/// genuinely ahead of this one, in the same oldest-first order QueueService
/// uses for prep-time estimation, so "position" and "remaining time" always
/// agree with each other and with the kitchen board.
/// </summary>
public class QueuePositionService : IQueuePositionService
{
    private readonly IQueueTokenRepository _tokens;
    private readonly IOrderRepository _orders;
    private readonly IQueueService _queue;

    public QueuePositionService(IQueueTokenRepository tokens, IOrderRepository orders, IQueueService queue)
    {
        _tokens = tokens;
        _orders = orders;
        _queue = queue;
    }

    public async Task<QueuePositionDto> GetMyPositionAsync(Guid orderId, Guid userId)
    {
        var order = await _orders.GetByIdAsync(orderId);
        if (order is null || order.UserId != userId)
        {
            throw new OrderException(ErrorKind.NotFound, "Order not found.");
        }

        var token = await _tokens.GetByOrderIdAsync(orderId)
            ?? throw new OrderException(ErrorKind.Conflict, "This order doesn't have a queue token yet — it may not be paid.");

        var active = await _tokens.GetActiveAsync();
        var waiting = active.Where(t => t.Status is QueueTokenStatus.Queued or QueueTokenStatus.Preparing).ToList();

        var position = token.Status == QueueTokenStatus.Ready
            ? 0
            : waiting.Count(t => IsAheadOf(t, token)) + 1;

        var dto = await _queue.GetTokenForOrderAsync(orderId)
            ?? throw new OrderException(ErrorKind.NotFound, "Token not found.");

        return new QueuePositionDto(orderId, token.Id, token.TokenNumber, token.Label, token.Status.ToString(),
            position, waiting.Count, token.EstimatedPrepMinutes, dto.RemainingMinutes);
    }

    public async Task<TokenBoardDto> GetBoardAsync()
    {
        var active = await _tokens.GetActiveAsync();
        var tokens = active.Select(t => new BoardTokenDto(t.TokenNumber, t.Label, t.Status.ToString())).ToList();
        return new TokenBoardDto(DateTimeOffset.UtcNow, tokens);
    }

    private static bool IsAheadOf(QueueToken other, QueueToken mine) =>
        other.Id != mine.Id && (other.CreatedAtUtc < mine.CreatedAtUtc || (other.CreatedAtUtc == mine.CreatedAtUtc && other.TokenNumber < mine.TokenNumber));
}
