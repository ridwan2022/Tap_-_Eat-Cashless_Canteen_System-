using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;

namespace TapAndEat.Api.Services;

public class FeedbackException : AppException
{
    public FeedbackException(ErrorKind kind, string message) : base(kind, message) { }
}

public class FeedbackService : IFeedbackService
{
    private readonly IFeedbackRepository _feedbacks;
    private readonly IOrderRepository _orders;
    private readonly TimeProvider _clock;

    public FeedbackService(IFeedbackRepository feedbacks, IOrderRepository orders, TimeProvider clock)
    {
        _feedbacks = feedbacks;
        _orders = orders;
        _clock = clock;
    }

    private static OrderFeedbackDto ToDto(OrderFeedback f) => new(
        f.Id, f.OrderId, f.UserId, f.UserName, f.Rating, f.Comment, f.MenuItemId, f.CreatedAtUtc, f.UpdatedAtUtc);

    public async Task<OrderFeedbackDto> SubmitFeedbackAsync(Guid userId, string userName, CreateFeedbackRequest request)
    {
        if (request.Rating < 1 || request.Rating > 5)
        {
            throw new FeedbackException(ErrorKind.Invalid, "Rating must be between 1 and 5 stars.");
        }

        var order = await _orders.GetByIdAsync(request.OrderId);
        if (order is null)
        {
            throw new FeedbackException(ErrorKind.NotFound, "Order not found.");
        }
        if (order.UserId != userId)
        {
            throw new FeedbackException(ErrorKind.Forbidden, "You can only rate your own orders.");
        }
        if (order.Status != OrderStatus.Collected && order.Status != OrderStatus.Paid)
        {
            throw new FeedbackException(ErrorKind.Invalid, "Only paid or collected orders can be rated.");
        }

        var now = _clock.GetUtcNow();
        var existing = await _feedbacks.GetByOrderIdAsync(request.OrderId);
        var feedback = existing ?? new OrderFeedback
        {
            OrderId = request.OrderId,
            UserId = userId,
            UserName = userName,
            CreatedAtUtc = now
        };

        feedback.Rating = request.Rating;
        feedback.Comment = (request.Comment ?? string.Empty).Trim();
        feedback.UpdatedAtUtc = now;
        feedback.UserName = userName;

        if (request.MenuItemId.HasValue)
        {
            feedback.MenuItemId = request.MenuItemId.Value;
        }
        else if (order.Lines.Count > 0 && !feedback.MenuItemId.HasValue)
        {
            feedback.MenuItemId = order.Lines[0].MenuItemId;
        }

        await _feedbacks.UpsertAsync(feedback);
        return ToDto(feedback);
    }

    public async Task<OrderFeedbackDto?> GetFeedbackByOrderIdAsync(Guid orderId, Guid userId)
    {
        var feedback = await _feedbacks.GetByOrderIdAsync(orderId);
        if (feedback is null) return null;
        if (feedback.UserId != userId)
        {
            throw new FeedbackException(ErrorKind.Forbidden, "You cannot view feedback for another user's order.");
        }
        return ToDto(feedback);
    }

    public async Task<IReadOnlyList<OrderFeedbackDto>> GetMyFeedbacksAsync(Guid userId) =>
        (await _feedbacks.GetByUserIdAsync(userId)).Select(ToDto).ToList();

    public async Task<FeedbackSummaryDto> GetSummaryAsync()
    {
        var data = await _feedbacks.GetSummaryAsync();
        return new FeedbackSummaryDto(
            data.AverageRating,
            data.TotalReviews,
            data.StarCounts,
            data.ItemRatings.Select(i => new MenuItemFeedbackRatingDto(i.MenuItemId, i.MenuItemName, i.AverageRating, i.ReviewCount)).ToList(),
            data.RecentReviews.Select(ToDto).ToList());
    }
}
