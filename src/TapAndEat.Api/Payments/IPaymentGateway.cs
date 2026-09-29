using TapAndEat.Api.Models;

namespace TapAndEat.Api.Payments;

public record GatewayInitRequest(Guid PaymentId, decimal Amount, string PayerReference, string CallbackUrl);

public record GatewayInitResult(bool Success, string? GatewayPaymentRef, string? RedirectUrl, string? Error)
{
    public static GatewayInitResult Ok(string gatewayRef, string redirectUrl) => new(true, gatewayRef, redirectUrl, null);
    public static GatewayInitResult Fail(string error) => new(false, null, null, error);
}

public enum GatewayOutcome
{
    /// <summary>The gateway hasn't decided (or we couldn't reach it). Nothing may be assumed — check again later.</summary>
    Pending,
    Success,
    Failed,
    Cancelled
}

/// <param name="CallbackParams">Query-string values the gateway appended when redirecting the customer back. Never trusted on their own — Success is only ever returned after a server-to-server confirmation.</param>
public record GatewayVerifyRequest(
    string GatewayPaymentRef,
    decimal ExpectedAmount,
    IReadOnlyDictionary<string, string> CallbackParams);

public record GatewayVerifyResult(GatewayOutcome Outcome, string? TransactionId, decimal? Amount, string? Error)
{
    public static GatewayVerifyResult Pending(string? note = null) => new(GatewayOutcome.Pending, null, null, note);
    public static GatewayVerifyResult Success(string? trxId, decimal? amount) => new(GatewayOutcome.Success, trxId, amount, null);
    public static GatewayVerifyResult Failed(string error) => new(GatewayOutcome.Failed, null, null, error);
    public static GatewayVerifyResult Cancelled() => new(GatewayOutcome.Cancelled, null, null, "The payment was cancelled.");
}

/// <summary>
/// Tasks 4.1/4.2 — one adapter per mobile-financial-service. Contract: adapters
/// never throw for gateway-side problems (timeouts, HTTP errors, declined
/// payments); they return a Fail/Pending result so the payment service can
/// record it and let the customer retry.
/// </summary>
public interface IPaymentGateway
{
    PaymentMethod Method { get; }
    Task<GatewayInitResult> InitiateAsync(GatewayInitRequest request, CancellationToken ct = default);
    Task<GatewayVerifyResult> VerifyAsync(GatewayVerifyRequest request, CancellationToken ct = default);
}

public interface IPaymentGatewayResolver
{
    /// <summary>Returns the adapter for a mobile-financial-service method (never <see cref="PaymentMethod.Wallet"/>).</summary>
    IPaymentGateway Resolve(PaymentMethod method);
}
