using System.Collections.Concurrent;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Payments;

public class MockPayment
{
    public string Ref { get; init; } = string.Empty;
    public PaymentMethod Method { get; init; }
    public decimal Amount { get; init; }
    public string CallbackUrl { get; init; } = string.Empty;

    /// <summary>null until the "customer" decides on the fake checkout page: success | failed | cancelled.</summary>
    public string? Outcome { get; set; }
    public string? TransactionId { get; set; }
}

/// <summary>
/// Shared state behind the simulated bKash/Nagad checkout, so the whole
/// payment flow can be demonstrated and tested without gateway credentials.
/// A singleton — the fake checkout page decides an outcome here and the
/// payment service later "verifies" it, exactly like a real gateway round trip.
/// </summary>
public class MockGatewayLedger
{
    private readonly ConcurrentDictionary<string, MockPayment> _payments = new();

    public MockPayment Create(PaymentMethod method, decimal amount, string callbackUrl)
    {
        var payment = new MockPayment
        {
            Ref = "MOCK-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant(),
            Method = method,
            Amount = amount,
            CallbackUrl = callbackUrl
        };
        _payments[payment.Ref] = payment;
        return payment;
    }

    public MockPayment? Get(string gatewayRef)
    {
        _payments.TryGetValue(gatewayRef, out var payment);
        return payment;
    }

    /// <summary>First decision wins; deciding twice is a no-op (false).</summary>
    public bool Decide(string gatewayRef, string outcome)
    {
        if (!_payments.TryGetValue(gatewayRef, out var payment)) return false;
        lock (payment)
        {
            if (payment.Outcome is not null) return false;
            payment.Outcome = outcome;
            if (outcome == "success")
            {
                payment.TransactionId = "MTX" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
            }
            return true;
        }
    }
}

public class MockPaymentGateway : IPaymentGateway
{
    private readonly MockGatewayLedger _ledger;

    public MockPaymentGateway(PaymentMethod method, MockGatewayLedger ledger)
    {
        Method = method;
        _ledger = ledger;
    }

    public PaymentMethod Method { get; }

    public Task<GatewayInitResult> InitiateAsync(GatewayInitRequest request, CancellationToken ct = default)
    {
        var payment = _ledger.Create(Method, request.Amount, request.CallbackUrl);
        var redirect = $"/mock-gateway.html?ref={Uri.EscapeDataString(payment.Ref)}";
        return Task.FromResult(GatewayInitResult.Ok(payment.Ref, redirect));
    }

    public Task<GatewayVerifyResult> VerifyAsync(GatewayVerifyRequest request, CancellationToken ct = default)
    {
        var payment = _ledger.Get(request.GatewayPaymentRef);
        if (payment is null)
        {
            return Task.FromResult(GatewayVerifyResult.Failed("Unknown payment reference."));
        }

        var result = payment.Outcome switch
        {
            "success" => GatewayVerifyResult.Success(payment.TransactionId, payment.Amount),
            "failed" => GatewayVerifyResult.Failed("The payment was declined."),
            "cancelled" => GatewayVerifyResult.Cancelled(),
            _ => GatewayVerifyResult.Pending("Customer has not completed the payment yet.")
        };
        return Task.FromResult(result);
    }
}
