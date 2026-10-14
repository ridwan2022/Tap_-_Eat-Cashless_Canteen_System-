using Microsoft.Extensions.Logging;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;

namespace TapAndEat.Api.Services;

public class NotificationService : INotificationService
{
    private readonly INotificationRepository _notifications;
    private readonly IUserRepository _users;
    private readonly IOrderRepository _orders;
    private readonly IQueueTokenRepository _tokens;
    private readonly IEventBroadcaster _events;
    private readonly TimeProvider _clock;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        INotificationRepository notifications,
        IUserRepository users,
        IOrderRepository orders,
        IQueueTokenRepository tokens,
        IEventBroadcaster events,
        TimeProvider clock,
        ILogger<NotificationService> logger)
    {
        _notifications = notifications;
        _users = users;
        _orders = orders;
        _tokens = tokens;
        _events = events;
        _clock = clock;
        _logger = logger;
    }

    private static NotificationDto ToDto(Notification n) => new(
        n.Id, n.Type, n.Channel, n.Title, n.Message, n.ReferenceId, n.IsRead, n.CreatedAtUtc, n.ReadAtUtc);

    public async Task<NotificationDto> CreateNotificationAsync(
        Guid userId, string type, string title, string message, string? referenceId = null, string channel = "InApp")
    {
        var n = new Notification
        {
            UserId = userId,
            Type = type,
            Title = title,
            Message = message,
            ReferenceId = referenceId,
            Channel = channel,
            CreatedAtUtc = _clock.GetUtcNow(),
            IsRead = false
        };

        await _notifications.AddAsync(n);

        // Broadcast live notification event over SSE
        _events.Publish(new ServerEvent("notification", new
        {
            id = n.Id,
            userId = n.UserId,
            type = n.Type,
            title = n.Title,
            message = n.Message,
            createdAtUtc = n.CreatedAtUtc
        }));

        // Log push simulation delivery (Task 11.4)
        var subs = await _notifications.GetPushSubscriptionsAsync(userId);
        if (subs.Count > 0)
        {
            _logger.LogInformation("Dispatched browser push notification to {Count} subscription(s) for user {UserId}: {Title}",
                subs.Count, userId, title);
        }

        return ToDto(n);
    }

    public async Task<IReadOnlyList<NotificationDto>> GetUserNotificationsAsync(Guid userId, bool? unreadOnly = null, int limit = 50) =>
        (await _notifications.GetByUserAsync(userId, unreadOnly, limit)).Select(ToDto).ToList();

    public Task<int> GetUnreadCountAsync(Guid userId) =>
        _notifications.GetUnreadCountAsync(userId);

    public async Task<bool> MarkAsReadAsync(Guid notificationId, Guid userId)
    {
        var ok = await _notifications.MarkAsReadAsync(notificationId, userId, _clock.GetUtcNow());
        if (ok)
        {
            _events.Publish(new ServerEvent("notification_read", new { id = notificationId, userId }));
        }
        return ok;
    }

    public async Task<int> MarkAllAsReadAsync(Guid userId)
    {
        var count = await _notifications.MarkAllAsReadAsync(userId, _clock.GetUtcNow());
        if (count > 0)
        {
            _events.Publish(new ServerEvent("notification_read_all", new { userId }));
        }
        return count;
    }

    public async Task AddPushSubscriptionAsync(Guid userId, PushSubscriptionRequest request)
    {
        var sub = new PushSubscription
        {
            UserId = userId,
            Endpoint = request.Endpoint,
            P256dh = request.P256dh,
            Auth = request.Auth,
            CreatedAtUtc = _clock.GetUtcNow()
        };
        await _notifications.AddPushSubscriptionAsync(sub);
    }

    public Task RemovePushSubscriptionAsync(Guid userId, string endpoint) =>
        _notifications.RemovePushSubscriptionAsync(userId, endpoint);

    public async Task NotifyOrderPaidAsync(Guid orderId, int tokenNumber, decimal orderTotal, Guid userId)
    {
        await CreateNotificationAsync(
            userId,
            NotificationType.OrderPaid,
            $"Payment Confirmed: Token #{tokenNumber:D3}",
            $"Your payment of ৳{orderTotal:F2} is confirmed! Your meal token is #{tokenNumber:D3}.",
            orderId.ToString());
    }

    public async Task NotifyTokenStatusChangedAsync(Guid tokenId, QueueTokenStatus status)
    {
        var token = await _tokens.GetByIdAsync(tokenId);
        if (token is null) return;

        var order = await _orders.GetByIdAsync(token.OrderId);
        if (order is null) return;

        if (status == QueueTokenStatus.Preparing)
        {
            await CreateNotificationAsync(
                order.UserId,
                NotificationType.TokenPreparing,
                $"Meal Being Prepared (Token #{token.TokenNumber:D3})",
                $"The kitchen has started preparing your order for token #{token.TokenNumber:D3}.",
                token.OrderId.ToString());
        }
        else if (status == QueueTokenStatus.Ready)
        {
            await CreateNotificationAsync(
                order.UserId,
                NotificationType.TokenReady,
                $"Order Ready for Pickup! (Token #{token.TokenNumber:D3})",
                $"Your food is ready! Please proceed to the counter with your token #{token.TokenNumber:D3} or RFID card.",
                token.OrderId.ToString());
        }
        else if (status == QueueTokenStatus.Collected)
        {
            await CreateNotificationAsync(
                order.UserId,
                NotificationType.OrderCollected,
                $"Meal Collected (Token #{token.TokenNumber:D3})",
                $"Enjoy your meal! Please take a moment to rate your experience.",
                token.OrderId.ToString());
        }
    }

    public async Task NotifyLowStockAsync(Guid ingredientId, string ingredientName, decimal currentStock, string unit)
    {
        var staffUsers = (await _users.GetAllAsync())
            .Where(u => u.IsActive && (u.Role == UserRole.Admin || u.Role == UserRole.KitchenStaff))
            .ToList();

        foreach (var staff in staffUsers)
        {
            await CreateNotificationAsync(
                staff.Id,
                NotificationType.LowStock,
                $"Low Stock Alert: {ingredientName}",
                $"Ingredient '{ingredientName}' is running low (Current level: {currentStock} {unit}). Please restock soon.",
                ingredientId.ToString());
        }
    }

    public async Task NotifySubsidyCreditedAsync(Guid userId, decimal amount, string frequency)
    {
        await CreateNotificationAsync(
            userId,
            NotificationType.SubsidyCredited,
            "Enterprise Subsidy Credited",
            $"Your {frequency.ToLowerInvariant()} meal subsidy of ৳{amount:F2} has been credited to your wallet balance.",
            null);
    }
}
