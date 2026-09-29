using Xunit;
using FluentAssertions;
using Moq;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;
using TapAndEat.Api.Services;

namespace TapAndEat.Tests;

public class CounterServiceTests
{
    private const string Card = "04A1B2C3";

    private readonly Mock<IWalletRepository> _walletsMock;
    private readonly Mock<IOrderRepository> _ordersMock;
    private readonly Mock<IUserRepository> _usersMock;
    private readonly Mock<IRfidTapRepository> _tapsMock;
    private readonly Mock<IOrderService> _orderServiceMock;
    private readonly Mock<IQueueService> _queueMock;
    private readonly Mock<IPaymentService> _paymentsMock;
    private readonly Mock<IPaymentTokenService> _paymentTokensMock;
    private readonly Mock<IEventBroadcaster> _eventsMock;
    private readonly Mock<TimeProvider> _clockMock;
    private readonly DateTimeOffset _now = new(2026, 9, 20, 4, 0, 0, TimeSpan.Zero);
    private readonly Guid _userId;
    private readonly Guid _staffId;
    private readonly CounterService _sut;

    public CounterServiceTests()
    {
        _walletsMock = new Mock<IWalletRepository>();
        _ordersMock = new Mock<IOrderRepository>();
        _usersMock = new Mock<IUserRepository>();
        _tapsMock = new Mock<IRfidTapRepository>();
        _orderServiceMock = new Mock<IOrderService>();
        _queueMock = new Mock<IQueueService>();
        _paymentsMock = new Mock<IPaymentService>();
        _paymentTokensMock = new Mock<IPaymentTokenService>();
        _eventsMock = new Mock<IEventBroadcaster>();
        _clockMock = new Mock<TimeProvider>();
        _clockMock.Setup(c => c.GetUtcNow()).Returns(_now);
        _userId = Guid.NewGuid();
        _staffId = Guid.NewGuid();

        _walletsMock.Setup(r => r.GetByCardUidAsync(Card))
            .ReturnsAsync(new Wallet { UserId = _userId, RfidCardUid = Card });
        _usersMock.Setup(r => r.GetByIdAsync(_userId))
            .ReturnsAsync(new User { Id = _userId, FullName = "Rahim" });

        _sut = new CounterService(
            _walletsMock.Object,
            _ordersMock.Object,
            _usersMock.Object,
            _tapsMock.Object,
            _orderServiceMock.Object,
            _queueMock.Object,
            _paymentsMock.Object,
            _paymentTokensMock.Object,
            _eventsMock.Object,
            _clockMock.Object);
    }

    private Order OrderWithStatus(OrderStatus status) => new()
    {
        UserId = _userId,
        Status = status,
        PaidAtUtc = _now.AddMinutes(-10)
    };

    private void GivenOrders(params Order[] orders) =>
        _ordersMock.Setup(r => r.GetByUserAsync(_userId)).ReturnsAsync(orders.ToList());

    private QueueTokenDto Token(Guid orderId, string status) =>
        new(Guid.NewGuid(), orderId, 7, "T-007", status, 10, 5, _now, new List<OrderLineSummary>());

    private void GivenToken(Order order, string status) =>
        _queueMock.Setup(q => q.GetTokenForOrderAsync(order.Id)).ReturnsAsync(Token(order.Id, status));

    // ============ Tap Tests ============

    [Fact]
    public async Task TapAsync_WithUnreadableCardId_RejectsAsUnknownCard()
    {
        var result = await _sut.TapAsync("zz", _staffId);

        result.Outcome.Should().Be("Rejected");
        result.Reason.Should().Be("UnknownCard");
        _tapsMock.Verify(t => t.AddAsync(
            It.Is<RfidTapEvent>(e => e.Outcome == TapOutcome.Rejected && e.Reason == TapReason.UnknownCard)), Times.Once);
    }

    [Fact]
    public async Task TapAsync_WithCardNotLinkedToAnyAccount_RejectsAsUnknownCard()
    {
        var result = await _sut.TapAsync("AABBCCDD", _staffId);

        result.Outcome.Should().Be("Rejected");
        result.Reason.Should().Be("UnknownCard");
        result.Message.Should().Contain("Unknown card");
    }

    [Fact]
    public async Task TapAsync_WithPaidAndReadyOrder_ApprovesAndMarksCollected()
    {
        var order = OrderWithStatus(OrderStatus.Paid);
        GivenOrders(order);
        GivenToken(order, "Ready");

        var result = await _sut.TapAsync(Card, _staffId);

        result.Outcome.Should().Be("Approved");
        result.CustomerName.Should().Be("Rahim");
        result.TokenLabel.Should().Be("T-007");
        _orderServiceMock.Verify(o => o.MarkCollectedAsync(order.Id), Times.Once);
        _queueMock.Verify(q => q.MarkCollectedAsync(order.Id), Times.Once);
        _eventsMock.Verify(e => e.Publish(It.Is<ServerEvent>(ev => ev.Type == "tap")), Times.Once);
    }

    [Fact]
    public async Task TapAsync_WithPaidButNotReadyOrder_RejectsAsOrderNotReady()
    {
        var order = OrderWithStatus(OrderStatus.Paid);
        GivenOrders(order);
        GivenToken(order, "Preparing");

        var result = await _sut.TapAsync(Card, _staffId);

        result.Outcome.Should().Be("Rejected");
        result.Reason.Should().Be("OrderNotReady");
        result.Message.Should().Contain("not ready yet");
        _orderServiceMock.Verify(o => o.MarkCollectedAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task TapAsync_WithOnlyUnpaidOrder_RejectsAsNoPaidOrder()
    {
        GivenOrders(OrderWithStatus(OrderStatus.PendingPayment));

        var result = await _sut.TapAsync(Card, _staffId);

        result.Outcome.Should().Be("Rejected");
        result.Reason.Should().Be("NoPaidOrder");
        result.Message.Should().Contain("not paid yet");
    }

    [Fact]
    public async Task TapAsync_RightAfterCollection_RejectsAsAlreadyCollected()
    {
        var order = OrderWithStatus(OrderStatus.Collected);
        order.CollectedAtUtc = _now.AddMinutes(-5);
        GivenOrders(order);

        var result = await _sut.TapAsync(Card, _staffId);

        result.Outcome.Should().Be("Rejected");
        result.Reason.Should().Be("AlreadyCollected");
    }

    // ============ VerifyManually Tests ============

    [Fact]
    public async Task VerifyManuallyAsync_WithUnknownTokenNumber_RejectsAsInvalidToken()
    {
        var result = await _sut.VerifyManuallyAsync(new ManualVerifyRequest(99, null), _staffId);

        result.Outcome.Should().Be("Rejected");
        result.Reason.Should().Be("InvalidToken");
        result.Source.Should().Be("Manual");
    }

    // ============ TapToPay Tests ============

    [Fact]
    public async Task TapToPayAsync_WithPendingOrder_PaysFromWalletAndApproves()
    {
        var order = OrderWithStatus(OrderStatus.PendingPayment);
        GivenOrders(order);
        var paid = new PaymentDto(Guid.NewGuid(), "Order", order.Id, 240m, "Wallet", "Succeeded",
            null, null, null, false, "signed-token", Token(order.Id, "Queued"), _now, _now);
        _paymentsMock
            .Setup(p => p.InitiateOrderPaymentAsync(_userId, order.Id, PaymentMethod.Wallet,
                $"tap:{order.Id:N}", string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paid);

        var result = await _sut.TapToPayAsync(Card, null, _staffId);

        result.Outcome.Should().Be("Approved");
        result.Source.Should().Be("TapToPay");
        result.TokenLabel.Should().Be("T-007");
    }

    [Fact]
    public async Task TapToPayAsync_WithInsufficientBalance_RejectsAsPaymentFailed()
    {
        var order = OrderWithStatus(OrderStatus.PendingPayment);
        GivenOrders(order);
        var failed = new PaymentDto(Guid.NewGuid(), "Order", order.Id, 240m, "Wallet", "Failed",
            null, "Insufficient wallet balance.", null, false, null, null, _now, _now);
        _paymentsMock
            .Setup(p => p.InitiateOrderPaymentAsync(_userId, order.Id, PaymentMethod.Wallet,
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failed);

        var result = await _sut.TapToPayAsync(Card, order.Id, _staffId);

        result.Outcome.Should().Be("Rejected");
        result.Reason.Should().Be("PaymentFailed");
        result.Message.Should().Contain("Insufficient");
    }

    // ============ GetRecent Tests ============

    [Fact]
    public async Task GetRecentAsync_ClampsCountToOneHundred()
    {
        _tapsMock.Setup(t => t.GetRecentAsync(It.IsAny<int>())).ReturnsAsync(new List<RfidTapEvent>());

        await _sut.GetRecentAsync(500);

        _tapsMock.Verify(t => t.GetRecentAsync(100), Times.Once);
    }
}