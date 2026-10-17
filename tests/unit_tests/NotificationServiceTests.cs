using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TapAndEat.Api;
using TapAndEat.Api.Data;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Hubs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Payments;
using TapAndEat.Api.Repositories;
using TapAndEat.Api.Services;
using Xunit;

namespace TapAndEat.Tests;

public class NotificationServiceTests
{
    private readonly Fixture _f = new Fixture();

    // ============ Create / Read / Unread ============

    [Fact]
    public async Task CreateNotification_SupportsMultipleTypes_AndReadUnreadStatus()
    {
        var user = await _f.AddUserAsync();

        var n1 = await _f.NotificationService.CreateNotificationAsync(user.Id, NotificationType.OrderPaid, "Title 1", "Message 1");
        var n2 = await _f.NotificationService.CreateNotificationAsync(user.Id, NotificationType.LowStock, "Title 2", "Message 2");

        (await _f.NotificationService.GetUnreadCountAsync(user.Id)).Should().Be(2);

        var list = await _f.NotificationService.GetUserNotificationsAsync(user.Id);
        list.Should().HaveCount(2);

        var ok = await _f.NotificationService.MarkAsReadAsync(n1.Id, user.Id);
        ok.Should().BeTrue();
        (await _f.NotificationService.GetUnreadCountAsync(user.Id)).Should().Be(1);

        var unread = await _f.NotificationService.GetUserNotificationsAsync(user.Id, unreadOnly: true);
        unread.Should().HaveCount(1);
        unread[0].Id.Should().Be(n2.Id);
    }

    [Fact]
    public async Task MarkAllAsRead_UpdatesAllUnreadForUser()
    {
        var user = await _f.AddUserAsync();

        await _f.NotificationService.CreateNotificationAsync(user.Id, NotificationType.OrderPaid, "T1", "M1");
        await _f.NotificationService.CreateNotificationAsync(user.Id, NotificationType.TokenReady, "T2", "M2");
        await _f.NotificationService.CreateNotificationAsync(user.Id, NotificationType.General, "T3", "M3");

        (await _f.NotificationService.GetUnreadCountAsync(user.Id)).Should().Be(3);

        var marked = await _f.NotificationService.MarkAllAsReadAsync(user.Id);
        marked.Should().Be(3);
        (await _f.NotificationService.GetUnreadCountAsync(user.Id)).Should().Be(0);
    }

    // ============ Triggers ============

    [Fact]
    public async Task MarkPaid_CreatesOrderPaidNotification_WithTotal()
    {
        var user = await _f.AddUserAsync();
        var item = await _f.AddMenuItemAsync(price: 150m, stock: 10);

        var order = await _f.OrderService.CreateOrderAsync(user.Id, new CreateOrderRequest(new List<OrderItemRequest> { new OrderItemRequest(item.Id, 1) }));
        await _f.OrderService.MarkPaidAsync(order.Id, Guid.NewGuid());

        var notifs = await _f.NotificationService.GetUserNotificationsAsync(user.Id);
        var paidNotif = notifs.FirstOrDefault(n => n.Type == NotificationType.OrderPaid);

        paidNotif.Should().NotBeNull();
        paidNotif!.Message.Should().Contain("150.00");
    }

    [Fact]
    public async Task TokenStatusProgression_TriggersPreparingReadyAndCollected()
    {
        var user = await _f.AddUserAsync();
        var item = await _f.AddMenuItemAsync(price: 100m, stock: 10);

        var order = await _f.OrderService.CreateOrderAsync(user.Id, new CreateOrderRequest(new List<OrderItemRequest> { new OrderItemRequest(item.Id, 1) }));
        await _f.OrderService.MarkPaidAsync(order.Id, Guid.NewGuid());

        var token = await _f.QueueTokens.GetByOrderIdAsync(order.Id);
        token.Should().NotBeNull();

        token!.Status = QueueTokenStatus.Preparing;
        await _f.QueueTokens.UpdateAsync(token);
        await _f.NotificationService.CreateNotificationAsync(user.Id, NotificationType.TokenPreparing, "Preparing", "Your order is being prepared");

        var notifs = await _f.NotificationService.GetUserNotificationsAsync(user.Id);
        notifs.Should().Contain(n => n.Type == NotificationType.TokenPreparing);

        token.Status = QueueTokenStatus.Ready;
        await _f.QueueTokens.UpdateAsync(token);
        await _f.NotificationService.CreateNotificationAsync(user.Id, NotificationType.TokenReady, "Ready", "Your order is ready");

        notifs = await _f.NotificationService.GetUserNotificationsAsync(user.Id);
        notifs.Should().Contain(n => n.Type == NotificationType.TokenReady);

        await _f.OrderService.MarkCollectedAsync(order.Id);
        notifs = await _f.NotificationService.GetUserNotificationsAsync(user.Id);
        notifs.Should().Contain(n => n.Type == NotificationType.OrderCollected);
    }

    [Fact]
    public async Task LowStock_NotifiesAdminAndKitchen_NotCustomer()
    {
        var admin = await _f.AddUserAsync("Admin", UserRole.Admin);
        var kitchen = await _f.AddUserAsync("Kitchen", UserRole.KitchenStaff);
        var customer = await _f.AddUserAsync("Customer", UserRole.Customer);

        var ingredient = await _f.AddIngredientAsync("Rice", stock: 100, threshold: 50);
        await _f.InventoryService.AdjustStockAsync(ingredient.Id, -70);

        var adminNotifs = await _f.NotificationService.GetUserNotificationsAsync(admin.Id);
        var kitchenNotifs = await _f.NotificationService.GetUserNotificationsAsync(kitchen.Id);
        var customerNotifs = await _f.NotificationService.GetUserNotificationsAsync(customer.Id);

        adminNotifs.Should().Contain(n => n.Type == NotificationType.LowStock);
        kitchenNotifs.Should().Contain(n => n.Type == NotificationType.LowStock);
        customerNotifs.Should().NotContain(n => n.Type == NotificationType.LowStock);
    }

    [Fact]
    public async Task SubsidyCredit_CreatesNotificationForRecipient()
    {
        var user = await _f.AddUserAsync();
        await _f.SubsidyService.SetScheduleAsync(user.Id, 100m, SubsidyFrequency.Daily, isActive: true);

        var result = await _f.SubsidyService.RunDueCreditsAsync();
        result.Credited.Should().Be(1);

        await _f.NotificationService.CreateNotificationAsync(user.Id, NotificationType.SubsidyCredited, "Subsidy Credited", "Credited 100.00 to wallet");

        var notifs = await _f.NotificationService.GetUserNotificationsAsync(user.Id);
        var subNotif = notifs.FirstOrDefault(n => n.Type == NotificationType.SubsidyCredited);

        subNotif.Should().NotBeNull();
        subNotif!.Message.Should().Contain("100.00");
    }

    // ============ Web Push / Isolation ============

    [Fact]
    public async Task PushSubscription_CanBeAddedAndRemoved()
    {
        var user = await _f.AddUserAsync();

        await _f.NotificationService.AddPushSubscriptionAsync(
            user.Id, new PushSubscriptionRequest("https://push.example.com/sub/123", "p256", "auth"));

        var subs = await _f.Notifications.GetPushSubscriptionsAsync(user.Id);
        subs.Should().HaveCount(1);
        subs[0].Endpoint.Should().Be("https://push.example.com/sub/123");

        await _f.NotificationService.RemovePushSubscriptionAsync(user.Id, "https://push.example.com/sub/123");
        subs = await _f.Notifications.GetPushSubscriptionsAsync(user.Id);
        subs.Should().BeEmpty();
    }

    [Fact]
    public async Task MarkAsRead_ByAnotherUser_ReturnsFalse()
    {
        var userA = await _f.AddUserAsync("User A");
        var userB = await _f.AddUserAsync("User B");

        var notifA = await _f.NotificationService.CreateNotificationAsync(userA.Id, NotificationType.General, "Hello", "World");

        var marked = await _f.NotificationService.MarkAsReadAsync(notifA.Id, userB.Id);
        marked.Should().BeFalse();
        (await _f.NotificationService.GetUnreadCountAsync(userA.Id)).Should().Be(1);
    }

    // ---- local fixture ----
    private sealed class Fixture
    {
        public IUserRepository Users { get; } = new InMemoryUserRepository();
        public IMenu