using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Payments;

/// <summary>
/// Task 4.2 — Nagad checkout adapter: initialize (encrypted/signed) → complete
/// (returns the URL Nagad's hosted page lives at) → verify on the callback.
/// Verification is a plain server-to-server GET of the payment reference, so a
/// forged redirect can't mark a payment successful. Written against Nagad's
/// published merchant API — see README for the sandbox checklist; it has not
/// been exercised against live sandbox credentials from the build environment.
/// </summary>
public class NagadGateway : IPaymentGateway
{
    private readonly HttpClient _http;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<NagadGateway> _logger;

    public NagadGateway(HttpClient http, IOptionsMonitor<PaymentOptions> options, ILogger<NagadGateway> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public PaymentMethod Method => PaymentMethod.Nagad;
    private NagadOptions Opt => _options.CurrentValue.Nagad;

    // Nagad order ids must be short and alphanumeric.
    private static string MerchantOrderId(Guid paymentId) => paymentId.ToString("N")[..20];

    public async Task<GatewayInitResult> InitiateAsync(GatewayInitRequest request, CancellationToken ct = default)
    {
        try
        {
            var orderId = MerchantOrderId(request.PaymentId);
            var challenge = Convert.ToHexString(RandomNumberGenerator.GetBytes(20));

            // Step 1 — initialize
            var initSensitive = JsonSerializer.Serialize(new
            {
                merchantId = Opt.MerchantId,
                datetime = Now(),
                orderId,
                challenge
            });
            using var initDoc = await PostAsync($"/check-out/initialize/{Opt.MerchantId}/{orderId}", new
            {
                dateTime = Now(),
                sensitiveData = NagadCrypto.Encrypt(initSensitive, Opt.PgPublicKey),
                signature = NagadCrypto.Sign(initSensitive, Opt.MerchantPrivateKey)
            }, ct);

            if (Str(initDoc.RootElement, "sensitiveData") is not { } encrypted)
            {
                return GatewayInitResult.Fail(Str(initDoc.RootElement, "message") ?? "Nagad rejected the payment request.");
            }

            using var initResult = JsonDocument.Parse(NagadCrypto.Decrypt(encrypted, Opt.MerchantPrivateKey));
            var paymentRefId = Str(initResult.RootElement, "paymentReferenceId");
            var serverChallenge = Str(initResult.RootElement, "challenge");
            if (paymentRefId is null || serverChallenge is null)
            {
                return GatewayInitResult.Fail("Nagad returned an unexpected response.");
            }

            // Step 2 — complete (yields the hosted checkout URL)
            var completeSensitive = JsonSerializer.Serialize(new
            {
                merchantId = Opt.MerchantId,
                orderId,
                currencyCode = "050",
                amount = request.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                challenge = serverChallenge
            });
            using var completeDoc = await PostAsync($"/check-out/complete/{paymentRefId}", new
            {
                sensitiveData = NagadCrypto.Encrypt(completeSensitive, Opt.PgPublicKey),
                signature = NagadCrypto.Sign(completeSensitive, Opt.MerchantPrivateKey),
                merchantCallbackURL = request.CallbackUrl,
                additionalMerchantInfo = new { payerReference = request.PayerReference }
            }, ct);

            var redirect = Str(completeDoc.RootElement, "callBackUrl");
            if (Str(completeDoc.RootElement, "status") != "Success" || redirect is null)
            {
                return GatewayInitResult.Fail(Str(completeDoc.RootElement, "message") ?? "Nagad could not start the payment.");
            }

            return GatewayInitResult.Ok(paymentRefId, redirect);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            _logger.LogError(ex, "Nagad key configuration is invalid.");
            return GatewayInitResult.Fail("Nagad is not configured correctly. Please contact the administrator.");
        }
        catch (Exception ex) when (BkashGateway.IsTransient(ex, ct))
        {
            _logger.LogWarning(ex, "Nagad initialize/complete failed (network/timeout).");
            return GatewayInitResult.Fail("Nagad did not respond in time. Nothing was charged — please try again.");
        }
    }

    public async Task<GatewayVerifyResult> VerifyAsync(GatewayVerifyRequest request, CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync(Url($"/verify/payment/{request.GatewayPaymentRef}"), ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            var root = doc.RootElement;

            switch (Str(root, "status"))
            {
                case "Success":
                    decimal? amount = decimal.TryParse(Str(root, "amount"), NumberStyles.Any, CultureInfo.InvariantCulture, out var a) ? a : null;
                    return GatewayVerifyResult.Success(Str(root, "issuerPaymentRefNo") ?? Str(root, "paymentRefId"), amount);
                case "Aborted":
                case "Cancelled":
                    return GatewayVerifyResult.Cancelled();
                case "Pending":
                case "Ongoing":
                case "InProgress":
                    return GatewayVerifyResult.Pending("Nagad hasn't completed the payment yet.");
                case null when !response.IsSuccessStatusCode:
                    return GatewayVerifyResult.Pending("Nagad could not confirm the payment yet.");
                default:
                    return GatewayVerifyResult.Failed(Str(root, "message") ?? "Nagad reported the payment failed.");
            }
        }
        catch (Exception ex) when (BkashGateway.IsTransient(ex, ct))
        {
            _logger.LogWarning(ex, "Nagad verify failed (network/timeout).");
            return GatewayVerifyResult.Pending("Nagad did not respond in time. Checking again shortly.");
        }
    }

    private async Task<JsonDocument> PostAsync(string path, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url(path));
        request.Headers.Add("X-KM-Api-Version", "v-0.2.0");
        request.Headers.Add("X-KM-IP-V4", "127.0.0.1");
        request.Headers.Add("X-KM-Client-Type", "PC_WEB");
        request.Content = JsonContent.Create(body);
        using var response = await _http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private string Url(string path) => Opt.BaseUrl.TrimEnd('/') + path;

    // Nagad expects the merchant's clock in yyyyMMddHHmmss (Bangladesh time).
    private static string Now() => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(6)).ToString("yyyyMMddHHmmss");

    private static string? Str(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()
            : null;
}
