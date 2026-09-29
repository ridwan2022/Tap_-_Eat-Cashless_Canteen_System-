namespace TapAndEat.Api.Infrastructure;

/// <summary>Bound from the "Payments" section of configuration.</summary>
public class PaymentOptions
{
    /// <summary>
    /// Absolute base URL gateways redirect the customer's browser back to
    /// (e.g. https://tapandeat.example.com). Blank = derive from the incoming request.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>HMAC secret for verified payment tokens (Task 4.4). Blank = random per process start. Set it via user-secrets/env in real use.</summary>
    public string TokenSecret { get; set; } = string.Empty;

    public decimal MaxTopUpAmount { get; set; } = 50_000m;

    public BkashOptions Bkash { get; set; } = new();
    public NagadOptions Nagad { get; set; } = new();
}

public static class GatewayModes
{
    /// <summary>In-process simulator with a fake hosted checkout page. No credentials, no network.</summary>
    public const string Mock = "Mock";

    /// <summary>The real adapter. Point BaseUrl at the gateway's sandbox or production host.</summary>
    public const string Live = "Live";
}

public class BkashOptions
{
    public string Mode { get; set; } = GatewayModes.Mock;
    public string BaseUrl { get; set; } = "https://tokenized.sandbox.bka.sh/v1.2.0-beta";
    public string AppKey { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 20;
}

public class NagadOptions
{
    public string Mode { get; set; } = GatewayModes.Mock;
    public string BaseUrl { get; set; } = "http://sandbox.mynagad.com:10080/remote-payment-gateway-1.0/api/dfs";
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>Merchant RSA private key, base64 PKCS#8 (no PEM header/footer).</summary>
    public string MerchantPrivateKey { get; set; } = string.Empty;

    /// <summary>Nagad payment-gateway RSA public key, base64 X.509 SubjectPublicKeyInfo (no PEM header/footer).</summary>
    public string PgPublicKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 20;
}

/// <summary>Bound from "Kitchen".</summary>
public class KitchenOptions
{
    /// <summary>Parallel cooking stations. Divides queued work when estimating prep time.</summary>
    public int Stations { get; set; } = 2;
}

/// <summary>Bound from "Orders".</summary>
public class OrderOptions
{
    /// <summary>How long an unpaid order holds its reserved stock before it expires.</summary>
    public int PaymentWindowMinutes { get; set; } = 15;
}

/// <summary>Bound from "Business". Bangladesh has no DST, so a fixed offset is enough.</summary>
public class BusinessOptions
{
    public double UtcOffsetHours { get; set; } = 6;
}

/// <summary>Bound from "Jobs".</summary>
public class JobsOptions
{
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 60;
}

public static class BusinessTime
{
    public static DateTimeOffset ToLocal(DateTimeOffset utc, double offsetHours) =>
        utc.ToOffset(TimeSpan.FromHours(offsetHours));

    public static string DayKey(DateTimeOffset utc, double offsetHours) =>
        ToLocal(utc, offsetHours).ToString("yyyy-MM-dd");
}
