using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Payments;
using TapAndEat.Api.Repositories;

namespace TapAndEat.Api.Services;

/// <summary>
/// Tasks 6.2–6.4 — meal release at the counter. A meal is handed over only for
/// a PAID order whose token is READY; everything else is rejected with a
/// reason the server can act on. Every tap — approved or not — is stored
/// (6.1) and pushed to connected screens over SSE (6.4).
/// </summary>
public class CounterService : ICounterService
{
    /// <summary>A second tap this soon after a collection is reported as "already collected".</summary>
    private static readonly TimeSpan DuplicateTapWindow = TimeSpan.FromMinutes(30);

    private readonly IWalletRepository _wallets;
    private readonly IOrderRepository _orders;
    private readonly IUserRepository _users;
    private readonly IRfidTapRepository _taps;
    private readonly IOrderService _orderService;
    private readonly IQueueService _queue;
    private readonly IPaymentService _payments;
    private readonly IPaymentTokenService _paymentTokens;
    private readonly IEventBroadcaster _events;
    private readonly TimeProvider _clock;

    public CounterService(
        IWalletRepository wallets,
        IOrderRepository orders,
        IUserRepository users,
        IRfidTapRepository taps,
        IOrderService orderService,
        IQueueService queue,
        IPaymentService payments,
        IPaymentTokenService paymentTokens,
        IEventBroadcaster events,
        TimeProvider clock)
    {
        _wallets = wallets;
        _orders = orders;
        _users = users;
        _taps = taps;
        _orderService = orderService;
        _queue = queue;
        _payments = payments;
        _paymentTokens = paymentTokens;
        _events = events;
        _clock = clock;
    }

    // ------------------------------------------------------------------ RFID tap

    public async Task<TapResultDto> TapAsync(string cardUid, Guid staffUserId)
    {
        if (!CardUid.TryNormalize(cardUid, out var uid))
        {
            return await RecordAsync("Rfid", cardUid?.Trim() ?? "", staffUserId, TapOutcome.Rejected, TapReason.UnknownCard,
                "That card ID couldn't be read. Tap again.");
        }

        var wallet = await _wallets.GetByCardUidAsync(uid);
        if (wallet is null)
        {
            return await RecordAsync("Rfid", uid, staffUserId, TapOutcome.Rejected, TapReason.UnknownCard,
                "Unknown card — it isn't linked to any account.");
        }

        var user = await _users.GetByIdAsync(wallet.UserId);
        using var _ = await KeyedLock.AcquireAsync($"user:{wallet.UserId}"); // serialises a double tap

        var orders = await _orders.GetByUserAsync(wallet.UserId);
        var paid = orders.Where(o => o.Status == OrderStatus.Paid).OrderBy(o => o.PaidAtUtc).ToList();

        if (paid.Count == 0)
        {
            var now = _clock.GetUtcNow();
            var recent = orders
                .Where(o => o.Status == OrderStatus.Collected && o.CollectedAtUtc >= now - DuplicateTapWindow)
                .OrderByDescending(o => o.CollectedAtUtc)
                .FirstOrDefault();
            if (recent is not null)
            {
                var token = await _queue.GetTokenForOrderAsync(recent.Id);
                return await RecordAsync("Rfid", uid, staffUserId, TapOutcome.Rejected, TapReason.AlreadyCollected,
                    $"Already collected — {token?.Label ?? "the order"} was handed over at {Local(recent.CollectedAtUtc!.Value)}.",
                    user, recent, token);
            }

            if (orders.Any(o => o.Status == OrderStatus.PendingPayment))
            {
                return await RecordAsync("Rfid", uid, staffUserId, TapOutcome.Rejected, TapReason.NoPaidOrder,
                    "Order not paid yet — payment is required before the meal is released.", user);
            }

            return await RecordAsync("Rfid", uid, staffUserId, TapOutcome.Rejected, TapReason.NoPaidOrder,
                "No paid order found for this card.", user);
        }

        // Oldest paid order that's ready wins.
        foreach (var order in paid)
        {
            var token = await _queue.GetTokenForOrderAsync(order.Id);
            if (token?.Status == QueueTokenStatus.Ready.ToString())
            {
                return await ReleaseAsync("Rfid", uid, staffUserId, user, order, token, extraReady: paid.Count - 1);
            }
        }

        var first = paid[0];
        var firstToken = await _queue.GetTokenForOrderAsync(first.Id);
        var eta = firstToken is null ? "" : $" (about {firstToken.RemainingMinutes} min)";
        return await RecordAsync("Rfid", uid, staffUserId, TapOutcome.Rejected, TapReason.OrderNotReady,
            $"{firstToken?.Label ?? "Order"} is paid but not ready yet{eta}.", user, first, firstToken);
    }

    // ------------------------------------------------------------------ manual

    public async Task<TapResultDto> VerifyManuallyAsync(ManualVerifyRequest request, Guid staffUserId)
    {
        Order? order = null;
        QueueTokenDto? token = null;

        if (!string.IsNullOrWhiteSpace(request.PaymentToken))
        {
            if (_paymentTokens.TryValidate(request.PaymentToken, out var orderId, out _))
            {
                order = await _orders.GetByIdAsync(orderId);
                if (order is not null && order.PaymentToken != request.PaymentToken.Trim()) order = null;
            }
        }
        else if (request.TokenNumber is { } number)
        {
            token = await _queue.GetTokenByNumberAsync(number);
            if (token is not null) order = await _orders.GetByIdAsync(token.OrderId);
        }
        else
        {
            return await RecordAsync("Manual", "", staffUserId, TapOutcome.Rejected, TapReason.InvalidToken,
                "Enter a token number or a payment token.");
        }

        if (order is null)
        {
            return await RecordAsync("Manual", "", staffUserId, TapOutcome.Rejected, TapReason.InvalidToken,
                "Token not recognised — it's invalid, forged or from another day.");
        }

        token ??= await _queue.GetTokenForOrderAsync(order.Id);
        var user = await _users.GetByIdAsync(order.UserId);

        using var userLock = await KeyedLock.AcquireAsync($"user:{order.UserId}");
        order = (await _orders.GetByIdAsync(order.Id))!; // re-read inside the lock

        switch (order.Status)
        {
            case OrderStatus.Collected:
                return await RecordAsync("Manual", "", staffUserId, TapOutcome.Rejected, TapReason.AlreadyCollected,
                    $"Already collected — {token?.Label ?? "the order"} was handed over at {Local(order.CollectedAtUtc!.Value)}.",
                    user, order, token);
            case OrderStatus.Paid when token?.Status == QueueTokenStatus.Ready.ToString():
                return await ReleaseAsync("Manual", "", staffUserId, user, order, token, extraReady: 0);
            case OrderStatus.Paid:
                var eta = token is null ? "" : $" (about {token.RemainingMinutes} min)";
                return await RecordAsync("Manual", "", staffUserId, TapOutcome.Rejected, TapReason.OrderNotReady,
                    $"{token?.Label ?? "Order"} is paid but not ready yet{eta}.", user, order, token);
            default:
                return await RecordAsync("Manual", "", staffUserId, TapOutcome.Rejected, TapReason.NoPaidOrder,
                    "Order not paid — payment is required before the meal is released.", user, order, token);
        }
    }

    // ------------------------------------------------------------------ tap to pay

    public async Task<TapResultDto> TapToPayAsync(string cardUid, Guid? orderId, Guid staffUserId)
    {
        if (!CardUid.TryNormalize(cardUid, out var uid) || await _wallets.GetByCardUidAsync(uid) is not { } wallet)
        {
            return await RecordAsync("TapToPay", cardUid?.Trim() ?? "", staffUserId, TapOutcome.Rejected, TapReason.UnknownCard,
                "Unknown card — it isn't linked to any account.");
        }

        var user = await _users.GetByIdAsync(wallet.UserId);
        var orders = await _orders.GetByUserAsync(wallet.UserId);
        var order = orderId is { } id
            ? orders.FirstOrDefault(o => o.Id == id)
            : orders.FirstOrDefault(o => o.Status == OrderStatus.PendingPayment); // newest first

        if (order is null || order.Status != OrderStatus.PendingPayment)
        {
            return await RecordAsync("TapToPay", uid, staffUserId, TapOutcome.Rejected, TapReason.NoPendingOrder,
                "No unpaid order to pay for this card.", user);
        }

        try
        {
            var payment = await _payments.InitiateOrderPaymentAsync(
                wallet.UserId, order.Id, PaymentMethod.Wallet, $"tap:{order.Id:N}", callbackBaseUrl: string.Empty);

            if (payment.Status != PaymentStatus.Succeeded.ToString())
            {
                return await RecordAsync("TapToPay", uid, staffUserId, TapOutcome.Rejected, TapReason.PaymentFailed,
                    payment.FailureReason ?? "Payment failed.", user, order);
            }

            return await RecordAsync("TapToPay", uid, staffUserId, TapOutcome.Approved, TapReason.None,
                $"Paid ৳{order.Total:0.##} from wallet. Token {payment.Token?.Label}.", user, order, payment.Token);
        }
        catch (PaymentException ex)
        {
            return await RecordAsync("TapToPay", uid, staffUserId, TapOutcome.Rejected, TapReason.PaymentFailed, ex.Message, user, order);
        }
    }

    public async Task<IReadOnlyList<TapResultDto>> GetRecentAsync(int count)
    {
        var taps = await _taps.GetRecentAsync(Math.Clamp(count, 1, 100));
        var result = new List<TapResultDto>();
        foreach (var tap in taps)
        {
            var user = tap.UserId is { } uid ? await _users.GetByIdAsync(uid) : null;
            result.Add(ToDto(tap, user?.FullName, null));
        }
        return result;
    }

    // ------------------------------------------------------------------ helpers

    private async Task<TapResultDto> ReleaseAsync(
        string source, string uid, Guid staffUserId, User? user, Order order, QueueTokenDto token, int extraReady)
    {
        await _orderService.MarkCollectedAsync(order.Id);
        await _queue.MarkCollectedAsync(order.Id);

        var more = extraReady > 0 ? $" ({extraReady} more paid order{(extraReady == 1 ? "" : "s")} for this customer)" : "";
        return await RecordAsync(source, uid, staffUserId, TapOutcome.Approved, TapReason.None,
            $"Release approved — {token.Label}{more}.", user, order, token);
    }

    private async Task<TapResultDto> RecordAsync(
        string source, string uid, Guid staffUserId, TapOutcome outcome, TapReason reason, string message,
        User? user = null, Order? order = null, QueueTokenDto? token = null)
    {
        var tap = new RfidTapEvent
        {
            Source = source,
            CardUid = uid,
            TappedAtUtc = _clock.GetUtcNow(),
            Outcome = outcome,
            Reason = reason,
            Message = message,
            UserId = user?.Id,
            OrderId = order?.Id ?? token?.OrderId,
            QueueTokenId = token?.Id,
            TokenNumber = token?.TokenNumber,
            StaffUserId = staffUserId
        };
        await _taps.AddAsync(tap);

        var dto = ToDto(tap, user?.FullName, order?.Lines.Select(l => new OrderLineSummary(l.Name, l.Quantity)).ToList()
            ?? token?.Items);
        _events.Publish(new ServerEvent("tap", dto));
        return dto;
    }

    private static TapResultDto ToDto(RfidTapEvent t, string? customerName, List<OrderLineSummary>? items) => new(
        t.Id, t.Outcome.ToString(), t.Reason.ToString(), t.Message, customerName,
        t.TokenNumber, t.TokenNumber is { } n ? $"T-{n:000}" : null,
        t.OrderId, items, t.TappedAtUtc, t.Source);

    private string Local(DateTimeOffset utc) => utc.ToOffset(TimeSpan.FromHours(6)).ToString("HH:mm");
}
