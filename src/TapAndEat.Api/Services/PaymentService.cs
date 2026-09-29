using Microsoft.Extensions.Options;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Payments;
using TapAndEat.Api.Repositories;

namespace TapAndEat.Api.Services;

/// <summary>
/// Tasks 4.1–4.4 — payment orchestration.
///
/// Guarantees:
///  * One charge per order: the same Idempotency-Key replays the stored
///    attempt; an already-paid order is refused; only one payment per order is
///    in flight (a repeat request for the same method returns it).
///  * A gateway's redirect is never believed — success only comes from the
///    gateway's own server-to-server confirmation, and its amount must match.
///  * Unknown outcomes (timeouts) leave the payment Initiated, not Failed, so
///    the customer is never told "failed" for a payment that went through.
///  * Money that arrives after its order was cancelled/expired/paid elsewhere
///    is credited to the customer's wallet rather than lost.
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _payments;
    private readonly IOrderRepository _orders;
    private readonly IOrderService _orderService;
    private readonly IWalletService _wallets;
    private readonly IQueueService _queue;
    private readonly IPaymentGatewayResolver _gateways;
    private readonly PaymentOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IPaymentRepository payments,
        IOrderRepository orders,
        IOrderService orderService,
        IWalletService wallets,
        IQueueService queue,
        IPaymentGatewayResolver gateways,
        IOptions<PaymentOptions> options,
        TimeProvider clock,
        ILogger<PaymentService> logger)
    {
        _payments = payments;
        _orders = orders;
        _orderService = orderService;
        _wallets = wallets;
        _queue = queue;
        _gateways = gateways;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    // ---------------------------------------------------------------- initiate

    public async Task<PaymentDto> InitiateOrderPaymentAsync(
        Guid userId, Guid orderId, PaymentMethod method, string? idempotencyKey, string callbackBaseUrl, CancellationToken ct = default)
    {
        idempotencyKey = NormalizeKey(idempotencyKey);
        using var _ = await KeyedLock.AcquireAsync($"pay-order:{orderId}");

        var order = await _orders.GetByIdAsync(orderId);
        if (order is null || order.UserId != userId)
        {
            throw new PaymentException(ErrorKind.NotFound, "Order not found.");
        }

        if (idempotencyKey is not null)
        {
            var replay = await _payments.GetByIdempotencyKeyAsync(userId, idempotencyKey);
            if (replay is not null)
            {
                if (replay.OrderId != orderId)
                {
                    throw new PaymentException(ErrorKind.Conflict, "This Idempotency-Key was already used for a different request.");
                }
                return await ToDtoAsync(replay);
            }
        }

        if (order.Status is OrderStatus.Paid or OrderStatus.Collected)
        {
            throw new PaymentException(ErrorKind.Conflict, "This order has already been paid.");
        }
        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new PaymentException(ErrorKind.Conflict, "This order was cancelled or has expired and can no longer be paid.");
        }

        var open = await _payments.GetInitiatedForOrderAsync(orderId);
        if (open is not null)
        {
            if (open.Method == method && method != PaymentMethod.Wallet)
            {
                return await ToDtoAsync(open); // same request again → the same in-flight payment, never a second one
            }

            open.Status = PaymentStatus.Cancelled;
            open.FailureReason = "Replaced by a new payment attempt.";
            open.CompletedAtUtc = _clock.GetUtcNow();
            await _payments.UpdateAsync(open);
        }

        var payment = new Payment
        {
            Purpose = PaymentPurpose.Order,
            UserId = userId,
            OrderId = orderId,
            Amount = order.Total,
            Method = method,
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = _clock.GetUtcNow()
        };
        await _payments.AddAsync(payment);

        return method == PaymentMethod.Wallet
            ? await PayWithWalletAsync(payment, order)
            : await StartGatewayAsync(payment, callbackBaseUrl, ct);
    }

    public async Task<PaymentDto> InitiateTopUpAsync(
        Guid userId, decimal amount, PaymentMethod method, string? idempotencyKey, string callbackBaseUrl, CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            throw new PaymentException(ErrorKind.Invalid, "Top-up amount must be greater than zero.");
        }
        if (decimal.Round(amount, 2) != amount)
        {
            throw new PaymentException(ErrorKind.Invalid, "Top-up amount can have at most 2 decimal places.");
        }
        if (amount > _options.MaxTopUpAmount)
        {
            throw new PaymentException(ErrorKind.Invalid, $"Top-up amount can't exceed ৳{_options.MaxTopUpAmount:0.##}.");
        }
        if (method == PaymentMethod.Wallet)
        {
            throw new PaymentException(ErrorKind.Invalid, "Choose bKash or Nagad to top up your wallet.");
        }

        idempotencyKey = NormalizeKey(idempotencyKey);
        using var _ = await KeyedLock.AcquireAsync($"pay-topup:{userId}");

        if (idempotencyKey is not null)
        {
            var replay = await _payments.GetByIdempotencyKeyAsync(userId, idempotencyKey);
            if (replay is not null)
            {
                if (replay.Purpose != PaymentPurpose.WalletTopUp)
                {
                    throw new PaymentException(ErrorKind.Conflict, "This Idempotency-Key was already used for a different request.");
                }
                return await ToDtoAsync(replay);
            }
        }

        var payment = new Payment
        {
            Purpose = PaymentPurpose.WalletTopUp,
            UserId = userId,
            Amount = amount,
            Method = method,
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = _clock.GetUtcNow()
        };
        await _payments.AddAsync(payment);
        return await StartGatewayAsync(payment, callbackBaseUrl, ct);
    }

    // ---------------------------------------------------------------- confirm

    public async Task<PaymentDto> HandleCallbackAsync(
        PaymentMethod method, Guid paymentId, IReadOnlyDictionary<string, string> callbackParams, CancellationToken ct = default)
    {
        var payment = await _payments.GetByIdAsync(paymentId);
        if (payment is null || payment.Method != method)
        {
            throw new PaymentException(ErrorKind.NotFound, "Payment not found.");
        }
        return await VerifyAndSettleAsync(payment, callbackParams, ct);
    }

    public async Task<PaymentDto> VerifyPaymentAsync(Guid userId, Guid paymentId, CancellationToken ct = default)
    {
        var payment = await GetOwnedAsync(userId, paymentId);
        return await VerifyAndSettleAsync(payment, new Dictionary<string, string>(), ct);
    }

    public async Task<PaymentDto> GetPaymentAsync(Guid userId, Guid paymentId) =>
        await ToDtoAsync(await GetOwnedAsync(userId, paymentId));

    public async Task<PaymentDto> CancelPaymentAsync(Guid userId, Guid paymentId)
    {
        var payment = await GetOwnedAsync(userId, paymentId);
        using var _ = await KeyedLock.AcquireAsync(LockKey(payment));

        payment = (await _payments.GetByIdAsync(paymentId))!;
        if (payment.Status != PaymentStatus.Initiated)
        {
            throw new PaymentException(ErrorKind.Conflict, $"This payment is already {payment.Status}.");
        }

        payment.Status = PaymentStatus.Cancelled;
        payment.FailureReason = "Cancelled by the customer.";
        payment.CompletedAtUtc = _clock.GetUtcNow();
        await _payments.UpdateAsync(payment);
        return await ToDtoAsync(payment);
    }

    // ---------------------------------------------------------------- internals

    private async Task<PaymentDto> StartGatewayAsync(Payment payment, string callbackBaseUrl, CancellationToken ct)
    {
        var gateway = _gateways.Resolve(payment.Method);
        var callbackUrl = $"{ResolveBase(callbackBaseUrl)}/api/payments/callback/{payment.Method.ToString().ToLowerInvariant()}?pid={payment.Id}";

        GatewayInitResult result;
        try
        {
            result = await gateway.InitiateAsync(
                new GatewayInitRequest(payment.Id, payment.Amount, payment.UserId.ToString("N"), callbackUrl), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Gateway adapter for {Method} threw while initiating payment {PaymentId}.", payment.Method, payment.Id);
            result = GatewayInitResult.Fail("The payment service is temporarily unavailable. Please try again.");
        }

        if (result.Success)
        {
            payment.GatewayPaymentRef = result.GatewayPaymentRef;
            payment.RedirectUrl = result.RedirectUrl;
        }
        else
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureReason = result.Error ?? "The payment could not be started.";
            payment.CompletedAtUtc = _clock.GetUtcNow();
        }

        await _payments.UpdateAsync(payment);
        return await ToDtoAsync(payment);
    }

    private async Task<PaymentDto> PayWithWalletAsync(Payment payment, Order order)
    {
        try
        {
            await _wallets.DebitAsync(order.UserId, order.Total, WalletTransactionType.Purchase,
                $"Order {ShortId(order.Id)}", $"order-pay:{order.Id:N}");
        }
        catch (WalletException ex)
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureReason = ex.Message;
            payment.CompletedAtUtc = _clock.GetUtcNow();
            await _payments.UpdateAsync(payment);
            return await ToDtoAsync(payment);
        }

        try
        {
            await _orderService.MarkPaidAsync(order.Id, payment.Id);
        }
        catch
        {
            // We took the money but couldn't confirm the order — give it back rather than lose it.
            await _wallets.CreditAsync(order.UserId, order.Total, WalletTransactionType.Refund,
                $"Refund for order {ShortId(order.Id)}", $"refund:{payment.Id:N}");
            throw;
        }

        payment.Status = PaymentStatus.Succeeded;
        payment.CompletedAtUtc = _clock.GetUtcNow();
        await _payments.UpdateAsync(payment);
        return await ToDtoAsync(payment);
    }

    private async Task<PaymentDto> VerifyAndSettleAsync(Payment payment, IReadOnlyDictionary<string, string> callbackParams, CancellationToken ct)
    {
        using var _ = await KeyedLock.AcquireAsync(LockKey(payment));

        // Re-read inside the lock: a concurrent callback may already have settled it.
        payment = (await _payments.GetByIdAsync(payment.Id))!;
        if (payment.Status != PaymentStatus.Initiated || string.IsNullOrEmpty(payment.GatewayPaymentRef))
        {
            return await ToDtoAsync(payment);
        }

        GatewayVerifyResult result;
        try
        {
            result = await _gateways.Resolve(payment.Method).VerifyAsync(
                new GatewayVerifyRequest(payment.GatewayPaymentRef, payment.Amount, callbackParams), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Gateway adapter for {Method} threw while verifying payment {PaymentId}.", payment.Method, payment.Id);
            result = GatewayVerifyResult.Pending("Could not confirm with the gateway yet.");
        }

        switch (result.Outcome)
        {
            case GatewayOutcome.Pending:
                return await ToDtoAsync(payment); // unknown → stay Initiated, can be re-verified

            case GatewayOutcome.Cancelled:
                payment.Status = PaymentStatus.Cancelled;
                payment.FailureReason = result.Error;
                break;

            case GatewayOutcome.Failed:
                payment.Status = PaymentStatus.Failed;
                payment.FailureReason = result.Error ?? "The payment failed.";
                break;

            case GatewayOutcome.Success:
                if (result.Amount is { } paid && paid != payment.Amount)
                {
                    _logger.LogError(
                        "Payment {PaymentId}: gateway confirmed ৳{Paid} but ৳{Expected} was expected. Marked failed for manual review (gateway ref {Ref}).",
                        payment.Id, paid, payment.Amount, payment.GatewayPaymentRef);
                    payment.Status = PaymentStatus.Failed;
                    payment.FailureReason = "The amount confirmed by the gateway did not match the order. Please contact support.";
                    break;
                }
                payment.GatewayTransactionId = result.TransactionId;
                await ApplySuccessAsync(payment);
                break;
        }

        payment.CompletedAtUtc = _clock.GetUtcNow();
        await _payments.UpdateAsync(payment);
        return await ToDtoAsync(payment);
    }

    private async Task ApplySuccessAsync(Payment payment)
    {
        if (payment.Purpose == PaymentPurpose.WalletTopUp)
        {
            await _wallets.CreditAsync(payment.UserId, payment.Amount, WalletTransactionType.TopUp,
                $"Top-up via {payment.Method}", $"topup:{payment.Id:N}");
            payment.Status = PaymentStatus.Succeeded;
            return;
        }

        var order = (await _orders.GetByIdAsync(payment.OrderId!.Value))!;
        if (order.Status == OrderStatus.PendingPayment)
        {
            await _orderService.MarkPaidAsync(order.Id, payment.Id);
            payment.Status = PaymentStatus.Succeeded;
            return;
        }

        // The money is real but the order can't take it any more (cancelled,
        // expired, or already settled by another payment) → wallet credit.
        await _wallets.CreditAsync(payment.UserId, payment.Amount, WalletTransactionType.Refund,
            $"Refund — order {ShortId(order.Id)} could no longer be paid", $"refund:{payment.Id:N}");
        payment.Status = PaymentStatus.Succeeded;
        payment.RefundedToWallet = true;
        payment.FailureReason = "The order was no longer payable; the amount was credited to your wallet.";
    }

    private async Task<Payment> GetOwnedAsync(Guid userId, Guid paymentId)
    {
        var payment = await _payments.GetByIdAsync(paymentId);
        if (payment is null || payment.UserId != userId)
        {
            throw new PaymentException(ErrorKind.NotFound, "Payment not found.");
        }
        return payment;
    }

    private static string LockKey(Payment p) =>
        p.Purpose == PaymentPurpose.Order ? $"pay-order:{p.OrderId}" : $"pay-topup:{p.UserId}";

    private static string? NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        key = key.Trim();
        if (key.Length > 100)
        {
            throw new PaymentException(ErrorKind.Invalid, "Idempotency-Key is too long (max 100 characters).");
        }
        return key;
    }

    private string ResolveBase(string requestBase) =>
        (string.IsNullOrWhiteSpace(_options.PublicBaseUrl) ? requestBase : _options.PublicBaseUrl).TrimEnd('/');

    private static string ShortId(Guid id) => id.ToString("N")[..8].ToUpperInvariant();

    private async Task<PaymentDto> ToDtoAsync(Payment p)
    {
        string? paymentToken = null;
        QueueTokenDto? token = null;

        if (p.Purpose == PaymentPurpose.Order && p is { Status: PaymentStatus.Succeeded, RefundedToWallet: false })
        {
            var order = await _orders.GetByIdAsync(p.OrderId!.Value);
            paymentToken = order?.PaymentToken;
            token = await _queue.GetTokenForOrderAsync(p.OrderId.Value);
        }

        return new PaymentDto(
            p.Id, p.Purpose.ToString(), p.OrderId, p.Amount, p.Method.ToString(), p.Status.ToString(),
            p.Status == PaymentStatus.Initiated ? p.RedirectUrl : null,
            p.FailureReason, p.GatewayTransactionId, p.RefundedToWallet,
            paymentToken, token, p.CreatedAtUtc, p.CompletedAtUtc);
    }
}
