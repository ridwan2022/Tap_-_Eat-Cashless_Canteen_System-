using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public interface IQueueTokenRepository
{
   
    Task<QueueToken> AddForOrderAsync(Guid orderId, string dayKey, Func<int, QueueToken> factory);
    Task<QueueToken?> GetByIdAsync(Guid id);
    Task<QueueToken?> GetByOrderIdAsync(Guid orderId);
    Task<QueueToken?> GetByNumberAsync(string dayKey, int tokenNumber);

    /// <summary>Tokens not yet collected (Queued, Preparing, Ready), oldest first.</summary>
    Task<IReadOnlyList<QueueToken>> GetActiveAsync();
    Task UpdateAsync(QueueToken token);
}
