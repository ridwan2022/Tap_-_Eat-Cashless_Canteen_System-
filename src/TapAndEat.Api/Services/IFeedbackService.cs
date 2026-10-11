using TapAndEat.Api.DTOs;

namespace TapAndEat.Api.Services;

public interface IFeedbackService
{
    Task<OrderFeedbackDto> SubmitFeedbackAsync(Guid userId, string userName, CreateFeedbackRequest request);
    Task<OrderFeedbackDto?> GetFeedbackByOrderIdAsync(Guid orderId, Guid userId);
    Task<IReadOnlyList<OrderFeedbackDto>> GetMyFeedbacksAsync(Guid userId);
    Task<FeedbackSummaryDto> GetSummaryAsync();
}
