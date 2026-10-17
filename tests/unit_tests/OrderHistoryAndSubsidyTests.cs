using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Hubs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;
using TapAndEat.Api.Services;
using Xunit;

namespace TapAndEat.Tests;

public class OrderHistoryAndSubsidyTests
{
    private readonly Fixture _f = new();

    // ============ History pagination / filters ============

    [Fact]
    public async Task GetOrderHistory_Pagination_ReturnsCorrectPageCountAndSize()
    {
        var user = await _f.AddUserAsync();
        var item = await _f.AddMenuItemAsync(price: 50m, stock: 100);

        for (var i = 0; i < 5; i++)
            await _f.OrderService.CreateOrderAsync(user.Id, new CreateOrderRequest(new List<OrderItemRequest> { new(item.Id, 1) }));

        var page1 = await _f.OrderService.GetOrderHistoryAsync(user.Id, page: 1, pageSize: 2);
        page1.TotalCount.Should().Be(5);
        page1.TotalPages.Should().Be(3);
        page1.Page.Should().Be(1);
        page1.Items.Should().HaveCount(2);

        var page3 = await _f.OrderService.GetOrderHistoryAsync(user.Id, page: 3, pageSize: 2);
        page3.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetOrderHistory_FiltersByStatus()
    {
        var user = await _f.AddUserAsync();
        var item = await _f.AddMenuItemAsync(price: 60m, stock: 100);

        var order1 = await _f.OrderService.CreateOrderAsync(user.Id, new CreateOrderRequest(new List<OrderItemRequest> { new(item.Id, 1) }));
        var order2 = await _f.OrderService.CreateOrderAsync(user.Id, new CreateOrderRequest(new List<OrderItemRequest> { new(item.Id, 1) }));
        await _f.OrderService.CreateOrderAsync(user.Id, new CreateOrderRequest(new List<OrderItemRequest> { new(item.Id, 1) }));

        await _f.OrderService.MarkPaidAsync(order1.Id, Guid.NewGuid());
        await _f.OrderService.CancelOrderAsync(order2.Id, user.Id);

        var paidOnly = await _f.OrderService.GetOrderHistoryAsync(user.Id, status: OrderStatus.Paid);
        paidOnly.TotalCount.Should().Be(1);
        paidOnly.Items[0].Id.Should().Be(order1.Id);

        var cancelledOnly = await _f.OrderService.GetOrderHistoryAsync(user.Id, status: OrderStatus.Cancelled);
        cancelledOnly.TotalCount.Should().Be(1);
        cancelledOnly.Items[0].Id.Should().Be(order2.Id);
    }

    [Fact]
    public async Task GetOrderHistory_FiltersByDateRange()
    {
        var user = await _f.AddUserAsync();
        var item = await _f.AddMenuItemAsync(price: 40m, stock: 100);
        var baseTime = _f.Clock.GetUtcNow();

        await _f.OrderService.CreateOrderAsync(user.Id, new CreateOrderRequest(new List<OrderItemRequest> { new(item.Id, 1) }));

        var matches = await _f.OrderService.GetOrderHistoryAsync(user.Id, from: baseTime.AddMinutes(-5), to: baseTime.AddMinutes(5));
        matches.TotalCount.Should().Be(1);

        var pastNoMatch = await _f.OrderService.GetOrderHistoryAsync(user.Id, from: baseTime.AddDays(-10), to: baseTime.AddDays(-5));
        pastNoMatch.TotalCount.Should().Be(0);
    }

    // ============ Subsidy summary / admin report ============

    [Fact]
    public async Task GetSubsidySummary_ReconcilesCreditedVsSpent()
    {
        var user = await _f.AddUserAsync();

        await _f.WalletService.CreditAsync(user.Id, 500m, WalletTransactionType.SubsidyCredit, "Enterprise Subsidy", "sub-1");
        await _f.WalletService.DebitAsync(user.Id, 120m, WalletTransactionType.Purchase, "Order #001", "ord-1");
        await _f.WalletService.DebitAsync(user.Id, 80m, WalletTransactionType.Purchase, "Order #002", "ord-2");

        var summary = await _f.WalletService.GetSubsidySummaryAsync(user.Id, "all");

        summary.TotalCredited.Should().Be(500m);
        summary.TotalSpent.Should().Be(200m);
        summary.CurrentBalance.Should().Be(300m);
        summary.SubsidizedOrdersCount.Should().Be(2);
        summary.Transactions.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetAdminSubsidyReport_CalculatesCompanyWideTotals()
    {
        var emp1 = await _f.AddUserAsync("Emp1");
        var emp2 = await _f.AddUserAsync("Emp2");

        await _f.SubsidyService.SetScheduleAsync(emp1.Id, 200m, SubsidyFrequency.Monthly, isActive: true);
        await _f.SubsidyService.SetScheduleAsync(emp2.Id, 300m, SubsidyFrequency.Monthly, isActive: true);

        await _f.WalletService.CreditAsync(emp1.Id, 200m, WalletTransactionType.SubsidyCredit, "Sub", "sub-e1");
        await _f.WalletService.CreditAsync(emp2.Id, 300m, WalletTransactionType.SubsidyCredit, "Sub", "sub-e2");
        await _f.WalletService.DebitAsync(emp1.Id, 50m, WalletTransactionType.Purchase, "Meal", "p-e1");

        var report = await _f.WalletService.GetAdminSubsidyReportAsync();

        report.TotalCompanySubsidyCredited.Should().Be(500m);
        report.TotalCompanySubsidySpent.Should().Be(50m);
        report.TotalActiveUsersWithSubsidy.Should().Be(2);
    }

    [Fact]
    public async Task GetOrderHistory_DoesNotLeakOtherUsersOrders()
    {
        var userA = await _f.AddUserAsync("UserA");
        var userB = await _f.AddUserAsync("UserB");
        var item = await _f.AddMenuItemAsync(price: 75m, stock: 100);

        await _f.OrderService.CreateOrderAsync(userA.Id, new CreateOrderRequest(new List<OrderItemRequest> { new(item.Id, 1) }));

        var userBHistory = await _f.OrderService.GetOrderHistoryAsync(userB.Id);
        userBHistory.TotalCount.Should().Be(0);
    }

    private sealed class Fixture
    {
        public IUserRepository Users { get; } = new InMemoryUserRepository();
        public IMenuRepository Menu { get; } = new InMemoryMenuRepository();
        public IOrderRepository Orders { get; } = new InMemoryOrderRepository();
        public IWalletRepository Wallets { get; } = new InMemoryWalletRepository();
        public IQueueTokenRepository QueueTokens { get; } = new InMemoryQueueTokenRepository();
        public INotificationRepository Notifications { get; } = new InMemoryNotificationRepository();
        public IEventBroadcaster Events { get; } = new EventBroadcaster();
        public NotificationServiceTests.FakeClock Clock { get; } = new();

        public IOrderService OrderService { get; }
        public IWalletService WalletService { get; }
        public ISubsidyService SubsidyService { get; }

        public Fixture()
        {
            var notificationService = new NotificationService(
                Notifications, Users, Orders, QueueTokens, Events, Clock, NullLogger<NotificationService>.Instance);

            var menuService = new MenuService(Menu, new NotificationServiceTests.NullHubContext());
            var paymentTokens = new PaymentTokenService(Options.Create(new PaymentOptions { TokenSecret = "test-secret" }));
            
            var queue = new QueueService(
                QueueTokens, Orders, Events,
                Options.Create(new KitchenOptions { Stations = 2 }),
                Options.Create(new BusinessOptions()), Clock);

            OrderService = new OrderService(
                Orders, Menu, menuService, queue, paymentTokens,
                Options.Create(new OrderOptions { PaymentWindowMinutes = 15 }), Clock, null, notificationService);

            WalletService = new WalletService(Wallets, Users, Clock);
            
            SubsidyService = new SubsidyService(
                Wallets, WalletService, Users, Options.Create(new BusinessOptions()), Clock);
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
    }
}