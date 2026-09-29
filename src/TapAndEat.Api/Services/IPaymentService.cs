using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Services;

public class PaymentException : AppException
{
    public PaymentException(ErrorKind kind, string message) : base(kind, message) { }
}

public interface IPaymentService
{
    /// <summary>
    /// Tasks 4.1–4.3 — start paying for an order. Method bKash/Nagad returns a
    /// redirect URL; Wallet settles immediately. Idempotent: the same
    /// idempotency key returns the original attempt, an order that is already
    /// paid is refused, and at most one payment per order is ever in flight.
    /// </summary>
    Task<PaymentDto> InitiateOrderPaymentAsync(
        Guid userId, Guid orderId, PaymentMethod method, string? idempotencyKey, string callbackBaseUrl, CancellationToken ct = default);

    /// <summary>Task 5.2 — top up the wallet through a mobile financial service.</summary>
    Task<PaymentDto> InitiateTopUpAsync(
        Guid userId, decimal amount, PaymentMethod method, string? idempotencyKey, string callbackBaseUrl, CancellationToken ct = default);

    /// <summary>The gateway redirected the customer back. Confirms with the gateway server-to-server before believing anything.</summary>
    Task<PaymentDto> HandleCallbackAsync(
        PaymentMethod method, Guid paymentId, IReadOnlyDictionary<string, string> callbackParams, CancellationToken ct = default);

    /// <summary>Re-checks an in-flight payment with the gateway (e.g. after a timeout). Safe to call repeatedly.</summary>
    Task<PaymentDto> VerifyPaymentAsync(Guid userId, Guid paymentId, CancellationToken ct = default);

    Task<PaymentDto> GetPaymentAsync(Guid userId, Guid paymentId);

    /// <summary>The customer walked away from an in-flight payment.</summary>
    Task<PaymentDto> CancelPaymentAsync(Guid userId, Guid paymentId);
}
