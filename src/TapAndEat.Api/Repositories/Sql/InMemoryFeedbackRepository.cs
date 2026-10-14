using System.Collections.Concurrent;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public class InMemoryFeedbackRepository : IFeedbackRepository
{
    private readonly ConcurrentDictionary<Guid, OrderFeedback> _feedbacks = new();
    private readonly IMenuRepository? _menu;

    public InMemoryFeedbackRepository(IMenuRepository? menu = null)
    {
        _menu = menu;
    }

    public Task<OrderFeedback> UpsertAsync(OrderFeedback feedback)
    {
        var existing = _feedbacks.Values.FirstOrDefault(f => f.OrderId == feedback.OrderId);
        if (existing is not null)
        {
            existing.Rating = feedback.Rating;
            existing.Comment = feedback.Comment;
            existing.UpdatedAtUtc = feedback.UpdatedAtUtc;
            if (feedback.MenuItemId.HasValue) existing.MenuItemId = feedback.MenuItemId;
            return Task.FromResult(existing);
        }

        _feedbacks[feedback.Id] = feedback;
        return Task.FromResult(feedback);
    }

    public Task<OrderFeedback?> GetByOrderIdAsync(Guid orderId)
    {
        var match = _feedbacks.Values.FirstOrDefault(f => f.OrderId == orderId);
        return Task.FromResult(match);
    }

    public Task<IReadOnlyList<OrderFeedback>> GetByUserIdAsync(Guid userId)
    {
        var list = _feedbacks.Values.Where(f => f.UserId == userId).OrderByDescending(f => f.CreatedAtUtc).ToList();
        return Task.FromResult<IReadOnlyList<OrderFeedback>>(list);
    }

    public Task<IReadOnlyList<OrderFeedback>> GetAllAsync(int limit = 100)
    {
        var list = _feedbacks.Values.OrderByDescending(f => f.CreatedAtUtc).Take(limit).ToList();
        return Task.FromResult<IReadOnlyList<OrderFeedback>>(list);
    }
