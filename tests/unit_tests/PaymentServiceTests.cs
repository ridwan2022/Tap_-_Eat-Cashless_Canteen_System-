using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Payments;
using TapAndEat.Api.Repositories;
using TapAndEat.Api.Services;

namespace TapAndEat.Tests;

public class PaymentServiceTests
{
    private readonly Mock<IPaymentRepository> _paymentsMock;
    private readonly Mock<IOrderRepository> _ordersMock;
    private readonly Mock<IOrderService> _orderServiceMock;
    private readonly Mock<IWalletService> _walletsMock;
    private readonly Mock<IQueueService> _queueMock;
    private readonly Mock<IPaymentGatewayResolver> _resolverMock;
    private readonly Mock<IPaymentGateway> _gatewayMock;
    private readonly Mock<ILogger<PaymentService>> _loggerMock;
    private readonly Mock<TimeProvider> _clockMock;
    private readonly DateTimeOffset _now = new(2026, 9, 20, 4, 0, 0, TimeSpan.Zero);
    private readonly Guid _userId;
    private readonly PaymentService _sut;

    public PaymentServiceTests()
    {
        _paymentsMock = new Mock<IPaymentRepository>();
        _ordersMock = new Mock<IOrderRepository>();
        _orderServiceMock = new Mock<IOrderService>();
        _walletsMock = new Mock<IWalletService>();
        _queueMock = new Mock<IQueueService>();
        _resolverMock = new Mock<IPaymentGatewayResolver>();
        _gatewayMock = new Mock<IPaymentGateway>();
        _loggerMock = new Mock<ILogger<PaymentService>>();
        _clockMock = new Mock<TimeProvider>();
        _clockMock.Setup(c => c.GetUtcNow()).Returns(_now);
        _userId = Guid.NewGuid();

        _resolverMock.Setup(r => r.Resolve(It.IsAny<PaymentMethod>())).Returns(_gatewayMock.Object);

        _sut = new PaymentService(
            _paymentsMock.Object,
            _ordersMock.Object,
            _orderServiceMock.Object,
            _walletsMock.Object,
            _queueMock.Object,
            _resolverMock.Object,
            Options.Create(new PaymentOptions { MaxTopUpAmount = 1000m }),
            _clockMock.Object,
            _loggerMock.Object);
    }

    // Total = 2 x 120 = 240
    private Order PendingOrder() => new()
    {
        UserId = _userId,
        ExpiresAtUtc = _now.AddMinutes(15),
        Lines = { new OrderLine { MenuItemId = Guid.NewGuid(), Name = "Biryani", UnitPrice = 120m, Quantity = 2, PrepTimeMinutes = 8 } }
    };

    private void GivenOrder(Order order) =>
        _ordersMock.Setup(r => r.GetByIdAsync(order.Id)).ReturnsAsync(order);

    private void GivenPayment(Payment payment) =>
        _paymentsMock.Setup(p => p.GetByIdAsync(payment.Id)).ReturnsAsync(payment);

    private void GivenGatewayVerifies(GatewayVerifyResult result) =>
        _gatewayMock
            .Setup(g => g.VerifyAsync(It.IsAny<GatewayVerifyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private Payment TopUpPayment() => new()
    {
        Purpose = PaymentPurpose.WalletTopUp,
        UserId = _userId,
        Amount = 500m,
        Method = PaymentMethod.bKash,
        GatewayPaymentRef = "ref-1"
    };

    private static WalletPosting Posting() =>
        new(new WalletTransactionDto(Guid.NewGuid(), "Purchase", -240m, 0m, "order", DateTimeOffset.UtcNow), true);

    // ============ InitiateOrderPayment Tests ============

    [Fact]
    public async Task InitiateOrderPaymentAsync_WithBkash_ReturnsRedirectUrl()
    {
        var order = PendingOrder();
        GivenOrder(order);
        _gatewayMock
            .Setup(g => g.InitiateAsync(It.IsAny<GatewayInitRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GatewayInitResult.Ok("ref-1", "https://gateway.test/pay"));

        var result = await _sut.InitiateOrderPaymentAsync(_userId, order.Id, PaymentMethod.bKash, "key-1", "https://tapandeat.test/");

        result.Status.Should().Be("Initiated");
        result.RedirectUrl.Should().Be("https://gateway.test/pay");
        result.Amount.Should().Be(240m);
        result.Method.Should().Be("bKash");
        _paymentsMock.Verify(p => p.AddAsync(It.IsAny<Payment>()), Times.Once);
        _gatewayMock.Verify(g => g.InitiateAsync(
            It.Is<GatewayInitRequest>(r => r.Amount == 240m &&
                r.CallbackUrl.StartsWith("https://tapandeat.test/api/payments/callback/bkash?pid=")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitiateOrderPaymentAsync_WithAlreadyPaidOrder_ThrowsConflict()
    {
        var order = PendingOrder();
        order.Status = OrderStatus.Paid;
        GivenOrder(order);

        Func<Task> act = () => _sut.InitiateOrderPaymentAsync(_userId, order.Id, PaymentMethod.bKash, null, "https://tapandeat.test");

        var ex = await act.Should().ThrowAsync<PaymentException>()
            .WithMessage("*already been paid*");
        ex.Which.Kind.Should().Be(ErrorKind.Conflict);
    }

    [Fact]
    public async Task InitiateOrderPaymentAsync_WithSameIdempotencyKey_ReturnsOriginalPayment()
    {
        var order = PendingOrder();
        GivenOrder(order);
        var existing = new Payment
        {
            Purpose = PaymentPurpose.Order,
            UserId = _userId,
            OrderId = order.Id,
            Amount = 240m,
            Method = PaymentMethod.bKash,
            IdempotencyKey = "key-1"
        };
        _paymentsMock.Setup(p => p.GetByIdempotencyKeyAsync(_userId, "key-1")).ReturnsAsync(existing);

        var result = await _sut.InitiateOrderPaymentAsync(_userId, order.Id, PaymentMethod.bKash, "key-1", "https://tapandeat.test");

        result.Id.Should().Be(existing.Id);
        _paymentsMock.Verify(p => p.AddAsync(It.IsAny<Payment>()), Times.Never);
        _gatewayMock.Verify(g => g.InitiateAsync(It.IsAny<GatewayInitRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InitiateOrderPaymentAsync_WithWalletAndEnoughBalance_SucceedsImmediately()
    {
        var order = PendingOrder();
        GivenOrder(order);
        _walletsMock
            .Setup(w => w.DebitAsync(_userId, 240m, WalletTransactionType.Purchase,
                It.IsAny<string>(), $"order-pay:{order.Id:N}"))
            .ReturnsAsync(Posting());

        var result = await _sut.InitiateOrderPaymentAsync(_userId, order.Id, PaymentMethod.Wallet, null, "https://tapandeat.test");

        result.Status.Should().Be("Succeeded");
        _orderServiceMock.Verify(o => o.MarkPaidAsync(order.Id, It.IsAny<Guid>()), Times.Once);
        _gatewayMock.Verify(g => g.InitiateAsync(It.IsAny<GatewayInitRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InitiateOrderPaymentAsync_WithWalletAndLowBalance_MarksPaymentFailed()
    {
        var order = PendingOrder();
        GivenOrder(order);
        _walletsMock
            .Setup(w => w.DebitAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<WalletTransactionType>(),
                It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new WalletException(ErrorKind.Invalid, "Insufficient wallet balance."));

        var result = await _sut.InitiateOrderPaymentAsync(_userId, order.Id, PaymentMethod.Wallet, null, "https://tapandeat.test");

        result.Status.Should().Be("Failed");
        result.FailureReason.Should().Contain("Insufficient");
        _orderServiceMock.Verify(o => o.MarkPaidAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task InitiateOrderPaymentAsync_WhenMarkPaidFailsAfterDebit_RefundsWallet()
    {
        var order = PendingOrder();
        GivenOrder(order);
        _walletsMock
            .Setup(w => w.DebitAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<WalletTransactionType>(),
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Posting());
        _orderServiceMock
            .Setup(o => o.MarkPaidAsync(order.Id, It.IsAny<Guid>()))
            .ThrowsAsync(new OrderException(ErrorKind.Conflict, "This order can no longer be paid."));

        Func<Task> act = () => _sut.InitiateOrderPaymentAsync(_userId, order.Id, PaymentMethod.Wallet, null, "https://tapandeat.test");

        await act.Should().ThrowAsync<OrderException>();
        _walletsMock.Verify(w => w.CreditAsync(_userId, 240m, WalletTransactionType.Refund,
            It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task InitiateOrderPaymentAsync_WhenGatewayThrows_MarksPaymentFailed()
    {
        var order = PendingOrder();
        GivenOrder(order);
        _gatewayMock
            .Setup(g => g.InitiateAsync(It.IsAny<GatewayInitRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("timeout"));

        var result = await _sut.InitiateOrderPaymentAsync(_userId, order.Id, PaymentMethod.Nagad, null, "https://tapandeat.test");

        result.Status.Should().Be("Failed");
        result.FailureReason.Should().Contain("temporarily unavailable");
    }

    // ============ InitiateTopUp Tests ============

    [Fact]
    public async Task InitiateTopUpAsync_WithAmountAboveLimit_ThrowsInvalid()
    {
        Func<Task> act = () => _sut.InitiateTopUpAsync(_userId, 5000m, PaymentMethod.bKash, null, "https://tapandeat.test");

        var ex = await act.Should().ThrowAsync<PaymentException>()
            .WithMessage("*exceed*");
        ex.Which.Kind.Should().Be(ErrorKind.Invalid);
        _paymentsMock.Verify(p => p.AddAsync(It.IsAny<Payment>()), Times.Never);
    }

    // ============ HandleCallback Tests ============

    [Fact]
    public async Task HandleCallbackAsync_WhenGatewayConfirmsTopUp_CreditsWallet()
    {
        var payment = TopUpPayment();
        GivenPayment(payment);
        GivenGatewayVerifies(GatewayVerifyResult.Success("TRX123", 500m));

        var result = await _sut.HandleCallbackAsync(PaymentMethod.bKash, payment.Id, new Dictionary<string, string>());

        result.Status.Should().Be("Succeeded");
        result.GatewayTransactionId.Should().Be("TRX123");
        _walletsMock.Verify(w => w.CreditAsync(_userId, 500m, WalletTransactionType.TopUp,
            It.IsAny<string>(), $"topup:{payment.Id:N}"), Times.Once);
    }

    [Fact]
    public async Task HandleCallbackAsync_WithAmountMismatch_MarksFailedAndDoesNotCredit()
    {
        var payment = TopUpPayment();
        GivenPayment(payment);
        GivenGatewayVerifies(GatewayVerifyResult.Success("TRX123", 100m));

        var result = await _sut.HandleCallbackAsync(PaymentMethod.bKash, payment.Id, new Dictionary<string, string>());

        result.Status.Should().Be("Failed");
        result.FailureReason.Should().Contain("did not match");
        _walletsMock.Verify(w => w.CreditAsync(It.IsAny<Guid>(), It.IsAny<decimal>(),
            It.IsAny<WalletTransactionType>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleCallbackAsync_WhenGatewayIsPending_KeepsPaymentInitiated()
    {
        var payment = TopUpPayment();
        GivenPayment(payment);
        GivenGatewayVerifies(GatewayVerifyResult.Pending("try later"));

        var result = await _sut.HandleCallbackAsync(PaymentMethod.bKash, payment.Id, new Dictionary<string, string>());

        result.Status.Should().Be("Initiated");
        _paymentsMock.Verify(p => p.UpdateAsync(It.IsAny<Payment>()), Times.Never);
    }

    [Fact]
    public async Task HandleCallbackAsync_WhenOrderNoLongerPayable_CreditsWalletInstead()
    {
        var order = PendingOrder();
        order.Status = OrderStatus.Cancelled;
        GivenOrder(order);
        var payment = new Payment
        {
            Purpose = PaymentPurpose.Order,
            UserId = _userId,
            OrderId = order.Id,
            Amount = 240m,
            Method = PaymentMethod.Nagad,
            GatewayPaymentRef = "ref-9"
        };
        GivenPayment(payment);
        GivenGatewayVerifies(GatewayVerifyResult.Success("TRX9", 240m));

        var result = await _sut.HandleCallbackAsync(PaymentMethod.Nagad, payment.Id, new Dictionary<string, string>());

        result.RefundedToWallet.Should().BeTrue();
        _orderServiceMock.Verify(o => o.MarkPaidAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        _walletsMock.Verify(w => w.CreditAsync(_userId, 240m, WalletTransactionType.Refund,
            It.IsAny<string>(), $"refund:{payment.Id:N}"), Times.Once);
    }
}
