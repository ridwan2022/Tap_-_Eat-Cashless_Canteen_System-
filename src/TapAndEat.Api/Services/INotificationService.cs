using TapAndEat.Api.DTOs;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Services;

public interface INotificationService
{
    Task<NotificationDto> CreateNotificationAsync(
        Guid userId, string type, string title, string message, string? referenceId = null, string channel = "InApp");
    Task<IReadOnlyList<NotificationDto>> GetUserNotificationsAsync(Guid userId, bool? unreadOnly = null, int limit = 50);
    Task<int> GetUnreadCountAsync(Guid userId);
    Task<bool> MarkAsReadAsync(Guid notificationId, Guid userId);
    Task<int> MarkAllAsReadAsync(Guid userId);
    Task AddPushSubscriptionAsync(Guid userId, PushSubscriptionRequest request);
    Task RemovePushSubscriptionAsync(Guid userId, string endpoint);

    // Triggers (Task 11.2)
    Task NotifyOrderPaidAsync(Guid orderId, int tokenNumber, decimal orderTotal, Guid userId);
    Task NotifyTokenStatusChangedAsync(Guid tokenId, QueueTokenStatus status);
    Task NotifyLowStockAsync(Guid ingredientId, string ingredientName, decimal currentStock, string unit);
    Task NotifySubsidyCreditedAsync(Guid userId, decimal amount, string frequency);
}
