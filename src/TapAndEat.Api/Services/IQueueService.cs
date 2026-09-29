using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Services;

public class QueueException : AppException
{
    public QueueException(ErrorKind kind, string message) : base(kind, message) { }
}

public interface IQueueService
{
    /// <summary>Task 7.2 — issues the order's token (idempotent: a paid order never gets a second one).</summary>
    Task<QueueToken> EnsureTokenForOrderAsync(Order order);

    Task<QueueTokenDto?> GetTokenForOrderAsync(Guid orderId);

    /// <summary>Today's token with this number, for manual lookup at the counter.</summary>
    Task<QueueTokenDto?> GetTokenByNumberAsync(int tokenNumber);

    /// <summary>Task 7.3 — every active token in order with a live remaining-time estimate.</summary>
    Task<KitchenBoardDto> GetBoardAsync();

    /// <summary>Kitchen moves a token forward: Queued → Preparing → Ready.</summary>
    Task<QueueTokenDto> SetStatusAsync(Guid tokenId, QueueTokenStatus newStatus);

    /// <summary>Called by the counter once the meal is handed over.</summary>
    Task MarkCollectedAsync(Guid orderId);
}
