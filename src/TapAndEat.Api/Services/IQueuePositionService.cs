using TapAndEat.Api.DTOs;

namespace TapAndEat.Api.Services;

public interface IQueuePositionService
{
    /// <summary>Task 8.1/8.2 — the caller's live position in the active queue for their own order.</summary>
    Task<QueuePositionDto> GetMyPositionAsync(Guid orderId, Guid userId);

    /// <summary>Task 8.3 — the public, PII-free token board (number + status only).</summary>
    Task<TokenBoardDto> GetBoardAsync();
}
