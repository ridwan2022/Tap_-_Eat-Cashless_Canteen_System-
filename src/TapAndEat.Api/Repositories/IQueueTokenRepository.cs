using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public interface IQueueTokenRepository
{
    /// <summary>
    /// Task 7.1/7.2 — returns the order's existing token, or atomically assigns
    /// the next number for the day and stores a new one. Guarantees exactly one
    /// token per order and a unique number per day, even under concurrency.
    /// </summary>
    Task<QueueToken> AddForOrderAsync(Guid orderId, string dayKey, Func<int, QueueToken> factory);
    Task<QueueToken?> GetByIdAsync(Guid id);
    Task<QueueToken?> GetByOrderIdAsync(Guid orderId);
    Task<QueueToken?> GetByNumberAsync(string dayKey, int tokenNumber);

    /// <summary>Tokens not yet collected (Queued, Preparing, Ready), oldest first.</summary>
    Task<IReadOnlyList<QueueToken>> GetActiveAsync();
    Task UpdateAsync(QueueToken token);
}
