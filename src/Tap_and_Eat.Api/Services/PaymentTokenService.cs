using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TapAndEat.Api.Infrastructure;

namespace TapAndEat.Api.Services;

/// <summary>
/// Task 4.4 — the "verified digital token" issued once a payment is confirmed.
/// It is self-verifying: order id + payment id + an HMAC of the two under a
/// server secret, so a forged or tampered token fails validation without any
/// lookup, and it can only be minted by code that saw a confirmed payment.
/// </summary>
public interface IPaymentTokenService
{
    string Issue(Guid orderId, Guid paymentId);
    bool TryValidate(string? token, out Guid orderId, out Guid paymentId);
}

public class PaymentTokenService : IPaymentTokenService
{
    private readonly byte[] _key;

    public PaymentTokenService(IOptions<PaymentOptions> options)
    {
        var secret = options.Value.TokenSecret;
        // No configured secret → random per process. Tokens then die with the
        // process, which matches the in-memory data store; set a real secret
        // (user-secrets / environment) once persistence is added.
        _key = string.IsNullOrWhiteSpace(secret) ? RandomNumberGenerator.GetBytes(32) : Encoding.UTF8.GetBytes(secret);
    }

    public string Issue(Guid orderId, Guid paymentId)
    {
        var payload = $"{orderId:N}.{paymentId:N}";
        return $"{payload}.{Sign(payload)}";
    }

    public bool TryValidate(string? token, out Guid orderId, out Guid paymentId)
    {
        orderId = paymentId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var parts = token.Trim().Split('.');
        if (parts.Length != 3) return false;
        if (!Guid.TryParseExact(parts[0], "N", out orderId) || !Guid.TryParseExact(parts[1], "N", out paymentId))
        {
            orderId = paymentId = Guid.Empty;
            return false;
        }

        var expected = Sign($"{parts[0]}.{parts[1]}");
        var ok = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[2]));
        if (!ok) orderId = paymentId = Guid.Empty;
        return ok;
    }

    private string Sign(string payload)
    {
        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(mac.AsSpan(0, 8)).ToLowerInvariant();
    }
}
