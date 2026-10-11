using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public interface INotificationRepository
{
    Task<Notification> AddAsync(Notification notification);
    Task<Notification?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<Notification>> GetByUserAsync(Guid userId, bool? unreadOnly = null, int limit = 50);
    Task<int> GetUnreadCountAsync(Guid userId);
    Task<bool> MarkAsReadAsync(Guid id, Guid userId, DateTimeOffset readAtUtc);
    Task<int> MarkAllAsReadAsync(Guid userId, DateTimeOffset readAtUtc);

    Task AddPushSubscriptionAsync(PushSubscription subscription);
    Task<IReadOnlyList<PushSubscription>> GetPushSubscriptionsAsync(Guid userId);
    Task RemovePushSubscriptionAsync(Guid userId, string endpoint);
}
