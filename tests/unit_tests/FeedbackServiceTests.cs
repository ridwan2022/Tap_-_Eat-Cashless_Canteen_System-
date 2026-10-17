using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;
using TapAndEat.Api.Services;
using Xunit;

namespace TapAndEat.Tests;

public class FeedbackServiceTests
{
    private readonly Fixture _f = new();

    // ============ Submit / validation ============

    [Fact]
    public async Task SubmitFeedback_OnCollectedOrder_Succeeds()
    {
        var user = await _f.AddUserAsync();
        var item = await _f.AddMenuItemAsync(price: 120m, stock: 10);

        var order = await _f.OrderService.CreateOrderAsync(user.Id, new CreateOrderRequest(new List<OrderItemRequest> { new(item.Id, 1) }));
        await _f.OrderService.MarkPaidAsync(order.Id, Guid.NewGuid());
        await _f.OrderService.MarkCollectedAsync(order.Id);

        var result = await _f.FeedbackService.SubmitFeedbackAsync(
            user.Id, "John Doe", new CreateFeedbackRequest(order.Id, 5, "Delicious khichuri!"));

        result.Rating.Should().Be(5);
        result.Comment.Should().Be("Delicious khichuri!");
        result.OrderId.Should().Be(order.Id);
        result.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task SubmitFeedback_OnUnpaidOrder_IsRejected()
    {
        var user = await _f.AddUserAsync();
        var item = await _f.AddMenuItemAsync(price: 80m, stock: 10);

        var order = await _f.OrderService.CreateOrderAsync(user.Id, new CreateOrderRequest(new List<OrderItemRequest> { new(item.Id, 1) }));

        var act = () => _f.FeedbackService.SubmitFeedbackAsync(
            user.Id, "John", new CreateFeedbackRequest(order.Id, 4, "Not paid yet"));

        var ex = await act.Should().ThrowAsync<FeedbackException>();
        ex.Which.Kind.Should().Be(ErrorKind.Invalid);
    }

    [Fact]
    public async Task SubmitFeedback_OnAnotherUsersOrder_IsForbidden()
    {
        var userA = await _f.AddUserAsync("UserA");
        var userB = await _f.AddUserAsync("UserB");
        var item = await _f.AddMenuItemAsync(price: 90m, stock: 10);

        var order = await _f.OrderService.CreateOrderAsync(userA.Id, new CreateOrderRequest(new List<OrderItemRequest> { new(item.Id, 1) }));
        await _f.OrderService.MarkPaidAsync(order.Id, Guid.NewGuid());

        var act = () => _f.FeedbackService.SubmitFeedbackAsync(
            userB.Id, "Imposter", new CreateFeedbackRequest(order.Id, 1, "Rude"));

        var ex = await act.Should().ThrowAsync<FeedbackException>();
        ex.Which.Kind.Should().Be(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task SubmitFeedback_RatingOutside1To5_IsRejected()
    {
        var user = await _f.AddUserAsync();
        var item = await _f.AddMenuItemAsync(price: 50m, stock: 10);

        var order = await _f.OrderService.CreateOrderAsync(user.Id, new CreateOrderRequest(new List<OrderItemRequest> { new(item.Id, 1) }));
        await _f.OrderService.MarkPaidAsync(order.Id, Guid.NewGuid());

        var act0 = () => _f.FeedbackService.SubmitFeedbackAsync(
            user.Id, "John", new CreateFeedbackRequest(order.Id, 0, "Zero stars"));
        (await act0.Should().ThrowAsync<FeedbackException>()).Which.Kind.Should().Be(ErrorKind.Invalid);

        var act6 = () => _f.FeedbackService.SubmitFeedbackAsync(
            user.Id, "John", new CreateFeedbackRequest(order.Id, 6, "Six stars"));
        (await act6.Should().ThrowAsync<FeedbackException>()).Which.Kind.Should().Be(ErrorKind.Invalid);
    }

    [Fact]
    public async Task SubmitFeedback_TwiceOnSameOrder_UpdatesInsteadOfDuplicate()
    {
        var user = await _f.AddUserAsync();
        var item = await _f.AddMenuItemAsync(price: 110m, stock: 10);

        var order = await _f.OrderService.CreateOrderAsync(user.Id, new CreateOrderRequest(new List<OrderItemRequest> { new(item.Id, 1) }));
        await _f.OrderService.MarkPaidAsync(order.Id, Guid.NewGuid());

        await _f.FeedbackService.SubmitFeedbackAsync(user.Id, "John", new CreateFeedbackRequest(order.Id, 3, "Average"));
        var updated = await _f.FeedbackService.SubmitFeedbackAsync(user.Id, "John", new CreateFeedbackRequest(order.Id, 4, "Actually great"));

        updated.Rating.Should().Be(4);
        updated.Comment.Should().Be("Actually great");

        var myReviews = await _f.FeedbackService.GetMyFeedbacksAsync(user.Id);
        myReviews.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetSummary_CalculatesAverageDistributionAndDishScores()
    {
        var user1 = await _f.AddUserAsync("U1");
        var user2 = await _f.AddUserAsync("U2");
        var dish1 = await _f.AddMenuItemAsync("Beef Tehari", price: 200m, stock: 10);

        var ord1 = await _f.OrderService.CreateOrderAsync(user1.Id, new CreateOrderRequest(new List<OrderItemRequest> { new(dish1.Id, 1) }));
        await _f.OrderService.MarkPaidAsync(ord1.Id, Guid.NewGuid());

        var ord2 = await _f.OrderService.CreateOrderAsync(user2.Id, new CreateOrderRequest(new List<OrderItemRequest> { new(dish1.Id, 1) }));
        await _f.OrderService.MarkPaidAsync(ord2.Id, Guid.NewGuid());

        await _f.FeedbackService.SubmitFeedbackAsync(user1.Id, "User 1", new CreateFeedbackRequest(ord1.Id, 5, "Amazing!"));
        await _f.FeedbackService.SubmitFeedbackAsync(user2.Id, "User 2", new CreateFeedbackRequest(ord2.Id, 3, "Decent"));

        var summary = await _f.FeedbackService.GetSummaryAsync();

        summary.TotalReviews.Should().Be(2);
        summary.AverageRating.Should().Be(4.0);
        summary.StarCounts[5].Should().Be(1);
        summary.StarCounts[3].Should().Be(1);
        summary.StarCounts[1].Should().Be(0);

        var dishSummary = summary.ItemRatings.FirstOrDefault(d => d.MenuItemId == dish1.Id);
        dishSummary.Should().NotBeNull();
        dishSummary!.AverageRating.Should().Be(4.0);
        dishSummary.ReviewCount.Should().Be(2);
    }

    private sealed class Fixture
    {
        public IUserRepository Users { get; } = new InMemoryUserRepository();
        public IMenuRepository Menu { get; } = new InMemoryMenuRepository();
        public IOrderRepository Orders { get; } = new InMemoryOrderRepository();
        public IQueueTokenRepository QueueTokens { get; } = new InMemoryQueueTokenRepository();
        public INotificationRepository Notifications { get; } = new InMemoryNotificationRepository();
        public IFeedbackRepository Feedbacks { get; } = new InMemoryFeedbackRepository();
        public IEventBroadcaster Events { get; } = new EventBroadcaster();
        public NotificationServiceTests.FakeClock Clock { get; } = new();

        public IFeedbackService FeedbackService { get; }
        public IOrderService OrderService { get; }

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

            FeedbackService = new FeedbackService(Feedbacks, Orders, Clock);
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