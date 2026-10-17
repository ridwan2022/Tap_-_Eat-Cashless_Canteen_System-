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
    private readonly Fixture _f = new();

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

        var order = await _f.OrderService.CreateOrderAsync(user.Id, new CreateOrderRequest(new List<OrderItemRequest> { new(item.Id, 1) }));
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

        var order = await _f.OrderService.CreateOrderAsync(user.Id, new CreateOrderRequest(new List<OrderItemRequest> { new(item.Id, 1) }));
        await _f.OrderService.MarkPaidAsync(order.Id, Guid.NewGuid());

        var token = await _f.QueueTokens.GetByOrderIdAsync(order.Id);
        token.Should().NotBeNull();

        await _f.QueueTokens.UpdateStatusAsync(token!.Id, QueueTokenStatus.Preparing);
        var notifs = await _f.NotificationService.GetUserNotificationsAsync(user.Id);
        notifs.Should().Contain(n => n.Type == NotificationType.TokenPreparing);

        await _f.QueueTokens.UpdateStatusAsync(token.Id, QueueTokenStatus.Ready);
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

    // ---- local fixture (kept inside unit_tests) ----
    private sealed class Fixture
    {
        public IUserRepository Users { get; } = new InMemoryUserRepository();
        public IMenuRepository Menu { get; } = new InMemoryMenuRepository();
        public IOrderRepository Orders { get; } = new InMemoryOrderRepository();
        public IPaymentRepository Payments { get; } = new InMemoryPaymentRepository();
        public IWalletRepository Wallets { get; } = new InMemoryWalletRepository();
        public IQueueTokenRepository QueueTokens { get; } = new InMemoryQueueTokenRepository();
        public INotificationRepository Notifications { get; } = new InMemoryNotificationRepository();
        public IIngredientRepository Ingredients { get; } = new InMemoryIngredientRepository();
        public IEventBroadcaster Events { get; } = new EventBroadcaster();
        public FakeClock Clock { get; } = new();

        public INotificationService NotificationService { get; }
        public IOrderService OrderService { get; }
        public ISubsidyService SubsidyService { get; }
        public IInventoryService InventoryService { get; }
        public IWalletService WalletService { get; }

        public Fixture()
        {
            NotificationService = new NotificationService(
                Notifications, Users, Orders, QueueTokens, Events, Clock, NullLogger<NotificationService>.Instance);

            var menuService = new MenuService(Menu, new NullHubContext());
            var paymentTokens = new PaymentTokenService(Options.Create(new PaymentOptions { TokenSecret = "test-secret" }));
            var queue = new QueueService(
                QueueTokens, Orders, Events,
                Options.Create(new KitchenOptions { Stations = 2 }),
                Options.Create(new BusinessOptions()), Clock, NotificationService);

            OrderService = new OrderService(
                Orders, Menu, menuService, queue, paymentTokens,
                Options.Create(new OrderOptions { PaymentWindowMinutes = 15 }), Clock, null, NotificationService);

            WalletService = new WalletService(Wallets, Users, Clock);
            SubsidyService = new SubsidyService(
                Wallets, WalletService, Users, Options.Create(new BusinessOptions()), Clock, NotificationService);
            InventoryService = new InventoryService(Ingredients, Menu, NullLogger<InventoryService>.Instance, NotificationService);
        }

        public async Task<User> AddUserAsync(string name = "Test User", UserRole role = UserRole.Customer)
        {
            var user = new User { FullName = name, Email = $"{Guid.NewGuid():N}@example.com", Role = role };
            await Users.AddAsync(user);
            return user;
        }

        public async Task<MenuItem> AddMenuItemAsync(string name = "Biryani", decimal price = 100m, int stock = 20)
        {
            var item = new MenuItem { Name = name, Price = price, StockCount = stock, PrepTimeMinutes = 5, IsPublished = true };
            await Menu.AddAsync(item);
            return item;
        }

        public async Task<Ingredient> AddIngredientAsync(string name, decimal stock, decimal threshold)
        {
            var i = new Ingredient { Name = name, Unit = "g", StockQuantity = stock, ReorderThreshold = threshold };
            await Ingredients.AddAsync(i);
            return i;
        }
    }

    public sealed class NullHubContext : Microsoft.AspNetCore.SignalR.IHubContext<MenuHub>
    {
        public Microsoft.AspNetCore.SignalR.IHubClients Clients { get; } = new NullClients();
        public Microsoft.AspNetCore.SignalR.IGroupManager Groups { get; } = new NullGroups();

        private sealed class NullClients : Microsoft.AspNetCore.SignalR.IHubClients
        {
            private readonly Microsoft.AspNetCore.SignalR.IClientProxy _p = new NullProxy();
            public Microsoft.AspNetCore.SignalR.IClientProxy All => _p;
            public Microsoft.AspNetCore.SignalR.IClientProxy AllExcept(IReadOnlyList<string> ids) => _p;
            public Microsoft.AspNetCore.SignalR.IClientProxy Client(string id) => _p;
            public Microsoft.AspNetCore.SignalR.IClientProxy Clients(IReadOnlyList<string> ids) => _p;
            public Microsoft.AspNetCore.SignalR.IClientProxy Group(string name) => _p;
            public Microsoft.AspNetCore.SignalR.IClientProxy GroupExcept(string name, IReadOnlyList<string> ids) => _p;
            public Microsoft.AspNetCore.SignalR.IClientProxy Groups(IReadOnlyList<string> names) => _p;
            public Microsoft.AspNetCore.SignalR.IClientProxy User(string id) => _p;
            public Microsoft.AspNetCore.SignalR.IClientProxy Users(IReadOnlyList<string> ids) => _p;
        }

        private sealed class NullProxy : Microsoft.AspNetCore.SignalR.IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken ct = default) => Task.CompletedTask;
        }

        private sealed class NullGroups : Microsoft.AspNetCore.SignalR.IGroupManager
        {
            public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken ct = default) => Task.CompletedTask;
            public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken ct = default) => Task.CompletedTask;
        }
    }

    public sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}