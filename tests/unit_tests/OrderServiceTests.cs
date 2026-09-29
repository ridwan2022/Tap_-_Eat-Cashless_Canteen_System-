using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Options;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;
using TapAndEat.Api.Services;

namespace TapAndEat.Tests;

public class OrderServiceTests
{
    private readonly Mock<IOrderRepository> _ordersMock;
    private readonly Mock<IMenuRepository> _menuRepoMock;
    private readonly Mock<IMenuService> _menuMock;
    private readonly Mock<IQueueService> _queueMock;
    private readonly Mock<IPaymentTokenService> _tokensMock;
    private readonly Mock<TimeProvider> _clockMock;
    private readonly DateTimeOffset _now = new(2026, 9, 20, 4, 0, 0, TimeSpan.Zero);
    private readonly Guid _userId;
    private readonly OrderService _sut;

    public OrderServiceTests()
    {
        _ordersMock = new Mock<IOrderRepository>();
        _menuRepoMock = new Mock<IMenuRepository>();
        _menuMock = new Mock<IMenuService>();
        _queueMock = new Mock<IQueueService>();
        _tokensMock = new Mock<IPaymentTokenService>();
        _clockMock = new Mock<TimeProvider>();
        _clockMock.Setup(c => c.GetUtcNow()).Returns(_now);
        _userId = Guid.NewGuid();

        _sut = new OrderService(
            _ordersMock.Object,
            _menuRepoMock.Object,
            _menuMock.Object,
            _queueMock.Object,
            _tokensMock.Object,
            Options.Create(new OrderOptions { PaymentWindowMinutes = 15 }),
            _clockMock.Object);
    }

    // One line: 2 x Biryani @ 120 = 240
    private Order PendingOrder(DateTimeOffset? expiresAt = null) => new()
    {
        UserId = _userId,
        ExpiresAtUtc = expiresAt ?? _now.AddMinutes(15),
        Lines = { new OrderLine { MenuItemId = Guid.NewGuid(), Name = "Biryani", UnitPrice = 120m, Quantity = 2, PrepTimeMinutes = 8 } }
    };

    private void GivenOrder(Order order) =>
        _ordersMock.Setup(r => r.GetByIdAsync(order.Id)).ReturnsAsync(order);

    private MenuItem GivenMenuItem()
    {
        var item = new MenuItem { Name = "Biryani", Price = 120m, PrepTimeMinutes = 8, IsPublished = true, StockCount = 10 };
        _menuRepoMock.Setup(r => r.GetByIdAsync(item.Id)).ReturnsAsync(item);
        return item;
    }

    // ============ CreateOrder Tests ============

    [Fact]
    public async Task CreateOrderAsync_WithValidItems_CreatesPendingOrderWithPriceSnapshot()
    {
        var item = GivenMenuItem();
        var request = new CreateOrderRequest(new List<OrderItemRequest> { new(item.Id, 2) });

        var result = await _sut.CreateOrderAsync(_userId, request);

        result.Status.Should().Be("PendingPayment");
        result.Total.Should().Be(240m);
        result.Lines.Should().ContainSingle();
        result.Lines[0].Name.Should().Be("Biryani");
        result.ExpiresAtUtc.Should().Be(_now.AddMinutes(15));
        _menuMock.Verify(m => m.ReserveStockAsync(It.IsAny<IReadOnlyList<StockLine>>()), Times.Once);
        _ordersMock.Verify(r => r.AddAsync(It.Is<Order>(o => o.UserId == _userId)), Times.Once);
    }

    [Fact]
    public async Task CreateOrderAsync_WithEmptyCart_ThrowsOrderException()
    {
        var request = new CreateOrderRequest(new List<OrderItemRequest>());

        Func<Task> act = () => _sut.CreateOrderAsync(_userId, request);

        var ex = await act.Should().ThrowAsync<OrderException>()
            .WithMessage("*empty*");
        ex.Which.Kind.Should().Be(ErrorKind.Invalid);
        _menuMock.Verify(m => m.ReserveStockAsync(It.IsAny<IReadOnlyList<StockLine>>()), Times.Never);
    }

    [Fact]
    public async Task CreateOrderAsync_WithQuantityAbove20_ThrowsOrderException()
    {
        var request = new CreateOrderRequest(new List<OrderItemRequest> { new(Guid.NewGuid(), 21) });

        Func<Task> act = () => _sut.CreateOrderAsync(_userId, request);

        await act.Should().ThrowAsync<OrderException>()
            .WithMessage("*between 1 and 20*");
    }

    [Fact]
    public async Task CreateOrderAsync_WithSameDishTwice_MergesIntoOneLine()
    {
        var item = GivenMenuItem();
        var request = new CreateOrderRequest(new List<OrderItemRequest> { new(item.Id, 2), new(item.Id, 3) });

        var result = await _sut.CreateOrderAsync(_userId, request);

        result.Lines.Should().ContainSingle();
        result.Lines[0].Quantity.Should().Be(5);
        result.Total.Should().Be(600m);
        _menuMock.Verify(m => m.ReserveStockAsync(
            It.Is<IReadOnlyList<StockLine>>(l => l.Count == 1 && l[0].Quantity == 5)), Times.Once);
    }

    [Fact]
    public async Task CreateOrderAsync_WhenStockReservationFails_ThrowsAndSavesNothing()
    {
        var item = GivenMenuItem();
        _menuMock
            .Setup(m => m.ReserveStockAsync(It.IsAny<IReadOnlyList<StockLine>>()))
            .ThrowsAsync(new MenuException("Biryani is out of stock."));
        var request = new CreateOrderRequest(new List<OrderItemRequest> { new(item.Id, 1) });

        Func<Task> act = () => _sut.CreateOrderAsync(_userId, request);

        var ex = await act.Should().ThrowAsync<OrderException>()
            .WithMessage("*out of stock*");
        ex.Which.Kind.Should().Be(ErrorKind.Invalid);
        _ordersMock.Verify(r => r.AddAsync(It.IsAny<Order>()), Times.Never);
    }

    // ============ GetOrder Tests ============

    [Fact]
    public async Task GetOrderAsync_ForAnotherCustomer_ThrowsNotFound()
    {
        var order = PendingOrder();
        GivenOrder(order);

        Func<Task> act = () => _sut.GetOrderAsync(order.Id, Guid.NewGuid(), isStaff: false);

        var ex = await act.Should().ThrowAsync<OrderException>()
            .WithMessage("*not found*");
        ex.Which.Kind.Should().Be(ErrorKind.NotFound);
    }

    [Fact]
    public async Task GetOrderAsync_ForStaff_ReturnsAnyOrder()
    {
        var order = PendingOrder();
        GivenOrder(order);

        var result = await _sut.GetOrderAsync(order.Id, Guid.NewGuid(), isStaff: true);

        result.Id.Should().Be(order.Id);
        result.Total.Should().Be(240m);
    }

    // ============ CancelOrder Tests ============

    [Fact]
    public async Task CancelOrderAsync_WithPendingOrder_CancelsAndReleasesStock()
    {
        var order = PendingOrder();
        GivenOrder(order);

        var result = await _sut.CancelOrderAsync(order.Id, _userId);

        result.Status.Should().Be("Cancelled");
        order.Status.Should().Be(OrderStatus.Cancelled);
        _menuMock.Verify(m => m.ReleaseStockAsync(
            It.Is<IReadOnlyList<StockLine>>(l => l.Count == 1 && l[0].Quantity == 2)), Times.Once);
    }

    [Fact]
    public async Task CancelOrderAsync_WithPaidOrder_ThrowsConflict()
    {
        var order = PendingOrder();
        order.Status = OrderStatus.Paid;
        GivenOrder(order);

        Func<Task> act = () => _sut.CancelOrderAsync(order.Id, _userId);

        var ex = await act.Should().ThrowAsync<OrderException>()
            .WithMessage("*counter staff*");
        ex.Which.Kind.Should().Be(ErrorKind.Conflict);
        _menuMock.Verify(m => m.ReleaseStockAsync(It.IsAny<IReadOnlyList<StockLine>>()), Times.Never);
    }

    // ============ MarkPaid Tests ============

    [Fact]
    public async Task MarkPaidAsync_WithPendingOrder_MarksPaidIssuesTokenAndQueuesOrder()
    {
        var order = PendingOrder();
        GivenOrder(order);
        var paymentId = Guid.NewGuid();
        _tokensMock.Setup(t => t.Issue(order.Id, paymentId)).Returns("signed-token");

        var result = await _sut.MarkPaidAsync(order.Id, paymentId);

        order.Status.Should().Be(OrderStatus.Paid);
        order.PaidByPaymentId.Should().Be(paymentId);
        result.PaymentToken.Should().Be("signed-token");
        _queueMock.Verify(q => q.EnsureTokenForOrderAsync(order), Times.Once);
    }

    [Fact]
    public async Task MarkPaidAsync_ReplayOfSamePayment_IsIdempotent()
    {
        var order = PendingOrder();
        var paymentId = Guid.NewGuid();
        order.Status = OrderStatus.Paid;
        order.PaidByPaymentId = paymentId;
        GivenOrder(order);

        var result = await _sut.MarkPaidAsync(order.Id, paymentId);

        result.Status.Should().Be("Paid");
        _ordersMock.Verify(r => r.UpdateAsync(It.IsAny<Order>()), Times.Never);
        _queueMock.Verify(q => q.EnsureTokenForOrderAsync(It.IsAny<Order>()), Times.Never);
    }

    // ============ ExpireStaleOrders Tests ============

    [Fact]
    public async Task ExpireStaleOrdersAsync_ExpiresOnlyOrdersPastPaymentWindow()
    {
        var stale = PendingOrder(expiresAt: _now.AddMinutes(-1));
        var fresh = PendingOrder(expiresAt: _now.AddMinutes(5));
        _ordersMock
            .Setup(r => r.GetByStatusAsync(OrderStatus.PendingPayment))
            .ReturnsAsync(new List<Order> { stale, fresh });
        GivenOrder(stale);

        var count = await _sut.ExpireStaleOrdersAsync();

        count.Should().Be(1);
        stale.Status.Should().Be(OrderStatus.Expired);
        fresh.Status.Should().Be(OrderStatus.PendingPayment);
        _menuMock.Verify(m => m.ReleaseStockAsync(It.IsAny<IReadOnlyList<StockLine>>()), Times.Once);
    }

    [Fact]
    public async Task ExpireStaleOrdersAsync_SkipsOrderPaidWhileWaiting()
    {
        var candidate = PendingOrder(expiresAt: _now.AddMinutes(-1));
        var latest = new Order { Id = candidate.Id, UserId = _userId, Status = OrderStatus.Paid };
        _ordersMock
            .Setup(r => r.GetByStatusAsync(OrderStatus.PendingPayment))
            .ReturnsAsync(new List<Order> { candidate });
        _ordersMock.Setup(r => r.GetByIdAsync(candidate.Id)).ReturnsAsync(latest);

        var count = await _sut.ExpireStaleOrdersAsync();

        count.Should().Be(0);
        latest.Status.Should().Be(OrderStatus.Paid);
        _menuMock.Verify(m => m.ReleaseStockAsync(It.IsAny<IReadOnlyList<StockLine>>()), Times.Never);
    }
}