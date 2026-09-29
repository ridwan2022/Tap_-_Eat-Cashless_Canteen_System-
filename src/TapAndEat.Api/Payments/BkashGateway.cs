using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Payments;

/// <summary>Cached grant token, shared across requests (bKash tokens live about an hour).</summary>
public class BkashTokenCache
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _idToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public async Task<string?> GetAsync(Func<Task<(string? Token, int ExpiresInSeconds)>> fetch)
    {
        if (_idToken is not null && _expiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _idToken;

        await _gate.WaitAsync();
        try
        {
            if (_idToken is not null && _expiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _idToken;
            var (token, expiresIn) = await fetch();
            if (token is null) return null;
            _idToken = token;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return token;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate() => _idToken = null;
}

/// <summary>
/// Task 4.1 — bKash "Tokenized Checkout" adapter: grant token → create payment
/// (customer is redirected to bKash) → execute payment on the callback. The
/// execute call is the server-to-server confirmation; the redirect's
/// status parameter is only used to short-circuit an obvious cancel/failure.
/// Written against bKash's published tokenized-checkout API — see README for
/// the sandbox checklist; it has not been exercised against live sandbox
/// credentials from the build environment.
/// </summary>
public class BkashGateway : IPaymentGateway
{
    private readonly HttpClient _http;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly BkashTokenCache _tokens;
    private readonly ILogger<BkashGateway> _logger;

    public BkashGateway(
        HttpClient http,
        IOptionsMonitor<PaymentOptions> options,
        BkashTokenCache tokens,
        ILogger<BkashGateway> logger)
    {
        _http = http;
        _options = options;
        _tokens = tokens;
        _logger = logger;
    }

    public PaymentMethod Method => PaymentMethod.bKash;
    private BkashOptions Opt => _options.CurrentValue.Bkash;

    public async Task<GatewayInitResult> InitiateAsync(GatewayInitRequest request, CancellationToken ct = default)
    {
        try
        {
            var token = await GetTokenAsync(ct);
            if (token is null)
            {
                return GatewayInitResult.Fail("Could not authenticate with bKash. Please try again shortly.");
            }

            var body = new
            {
                mode = "0011",
                payerReference = request.PayerReference,
                callbackURL = request.CallbackUrl,
                amount = request.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                currency = "BDT",
                intent = "sale",
                merchantInvoiceNumber = request.PaymentId.ToString("N")
            };

            using var doc = await PostAsync("/tokenized/checkout/create", body, token, ct);
            var root = doc.RootElement;
            if (Str(root, "statusCode") != "0000" || Str(root, "bkashURL") is not { } url || Str(root, "paymentID") is not { } id)
            {
                return GatewayInitResult.Fail(Str(root, "statusMessage") ?? "bKash rejected the payment request.");
            }

            return GatewayInitResult.Ok(id, url);
        }
        catch (Exception ex) when (IsTransient(ex, ct))
        {
            _logger.LogWarning(ex, "bKash create-payment failed (network/timeout).");
            return GatewayInitResult.Fail("bKash did not respond in time. Nothing was charged — please try again.");
        }
    }

    public async Task<GatewayVerifyResult> VerifyAsync(GatewayVerifyRequest request, CancellationToken ct = default)
    {
        request.CallbackParams.TryGetValue("status", out var status);
        if (string.Equals(status, "cancel", StringComparison.OrdinalIgnoreCase)) return GatewayVerifyResult.Cancelled();
        if (string.Equals(status, "failure", StringComparison.OrdinalIgnoreCase)) return GatewayVerifyResult.Failed("bKash reported the payment failed.");

        try
        {
            var token = await GetTokenAsync(ct);
            if (token is null) return GatewayVerifyResult.Pending("Could not reach bKash to confirm the payment yet.");

            using var executed = await PostAsync("/tokenized/checkout/execute", new { paymentID = request.GatewayPaymentRef }, token, ct);
            var result = ParseFinal(executed.RootElement);
            if (result is not null) return result;

            // Execute can fail when it was already executed (e.g. a retry). The query endpoint is the source of truth.
            using var queried = await PostAsync("/tokenized/checkout/payment/status", new { paymentID = request.GatewayPaymentRef }, token, ct);
            return ParseFinal(queried.RootElement)
                ?? (Str(queried.RootElement, "transactionStatus") is "Initiated" or "Authorized"
                    ? GatewayVerifyResult.Pending("bKash hasn't completed the payment yet.")
                    : GatewayVerifyResult.Failed(Str(queried.RootElement, "statusMessage") ?? "bKash could not complete the payment."));
        }
        catch (Exception ex) when (IsTransient(ex, ct))
        {
            // Unknown outcome — never assume failure OR success. Stay Pending so the payment can be re-verified.
            _logger.LogWarning(ex, "bKash execute/query failed (network/timeout).");
            return GatewayVerifyResult.Pending("bKash did not respond in time. Checking again shortly.");
        }
    }

    private static GatewayVerifyResult? ParseFinal(JsonElement root)
    {
        if (Str(root, "statusCode") == "0000" && Str(root, "transactionStatus") == "Completed")
        {
            decimal? amount = decimal.TryParse(Str(root, "amount"), NumberStyles.Any, CultureInfo.InvariantCulture, out var a) ? a : null;
            return GatewayVerifyResult.Success(Str(root, "trxID"), amount);
        }
        return null;
    }

    private async Task<string?> GetTokenAsync(CancellationToken ct)
    {
        return await _tokens.GetAsync(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Url("/tokenized/checkout/token/grant"));
            request.Headers.Add("username", Opt.Username);
            request.Headers.Add("password", Opt.Password);
            request.Content = JsonContent.Create(new { app_key = Opt.AppKey, app_secret = Opt.AppSecret });
            using var response = await _http.SendAsync(request, ct);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var idToken = Str(doc.RootElement, "id_token");
            var expires = int.TryParse(Str(doc.RootElement, "expires_in"), out var e) ? e : 3600;
            return (idToken, expires);
        });
    }

    private async Task<JsonDocument> PostAsync(string path, object body, string idToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url(path));
        request.Headers.TryAddWithoutValidation("Authorization", idToken);
        request.Headers.Add("X-APP-Key", Opt.AppKey);
        request.Content = JsonContent.Create(body);
        using var response = await _http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private string Url(string path) => Opt.BaseUrl.TrimEnd('/') + path;

    private static string? Str(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()
            : null;

    internal static bool IsTransient(Exception ex, CancellationToken ct) =>
        (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
        && !ct.IsCancellationRequested;
}
