using System.Collections.Concurrent;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public class InMemoryNotificationRepository : INotificationRepository
{
    private readonly ConcurrentDictionary<Guid, Notification> _notifications = new();
    private readonly ConcurrentDictionary<Guid, PushSubscription> _subscriptions = new();

    public Task<Notification> AddAsync(Notification notification)
    {
        _notifications[notification.Id] = notification;
        return Task.FromResult(notification);
    }

    public Task<Notification?> GetByIdAsync(Guid id)
    {
        _notifications.TryGetValue(id, out var n);
        return Task.FromResult(n);
    }

    public Task<IReadOnlyList<Notification>> GetByUserAsync(Guid userId, bool? unreadOnly = null, int limit = 50)
    {
        var query = _notifications.Values.Where(n => n.UserId == userId);
        if (unreadOnly == true)
        {
            query = query.Where(n => !n.IsRead);
        }
        var list = query.OrderByDescending(n => n.CreatedAtUtc).Take(limit).ToList();
        return Task.FromResult<IReadOnlyList<Notification>>(list);
    }

    public Task<int> GetUnreadCountAsync(Guid userId)
    {
        var count = _notifications.Values.Count(n => n.UserId == userId && !n.IsRead);
        return Task.FromResult(count);
    }

    public Task<bool> MarkAsReadAsync(Guid id, Guid userId, DateTimeOffset readAtUtc)
    {
        if (_notifications.TryGetValue(id, out var n) && n.UserId == userId)
        {
            n.IsRead = true;
            n.ReadAtUtc = readAtUtc;
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<int> MarkAllAsReadAsync(Guid userId, DateTimeOffset readAtUtc)
    {
        var updated = 0;
        foreach (var n in _notifications.Values.Where(n => n.UserId == userId && !n.IsRead))
        {
            n.IsRead = true;
            n.ReadAtUtc = readAtUtc;
            updated++;
        }
        return Task.FromResult(updated);
    }

    public Task AddPushSubscriptionAsync(PushSubscription subscription)
    {
        // Replace existing subscription for same user & endpoint
        var existing = _subscriptions.Values.FirstOrDefault(s => s.UserId == subscription.UserId && s.Endpoint == subscription.Endpoint);
        if (existing is not null)
        {
            _subscriptions.TryRemove(existing.Id, out _);
        }
        _subscriptions[subscription.Id] = subscription;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PushSubscription>> GetPushSubscriptionsAsync(Guid userId)
    {
        var subs = _subscriptions.Values.Where(s => s.UserId == userId).ToList();
        return Task.FromResult<IReadOnlyList<PushSubscription>>(subs);
    }

    public Task RemovePushSubscriptionAsync(Guid userId, string endpoint)
    {
        var match = _subscriptions.Values.FirstOrDefault(s => s.UserId == userId && s.Endpoint == endpoint);
        if (match is not null)
        {
            _subscriptions.TryRemove(match.Id, out _);
        }
        return Task.CompletedTask;
    }
}
