using Microsoft.Extensions.Options;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Payments;

/// <summary>
/// Picks Mock or real adapter per method from configuration
/// (Payments:Bkash:Mode / Payments:Nagad:Mode), so flipping a gateway to its
/// live/sandbox adapter is a config change, not a code change.
/// </summary>
public class PaymentGatewayResolver : IPaymentGatewayResolver
{
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly MockGatewayLedger _ledger;
    private readonly BkashGateway _bkash;
    private readonly NagadGateway _nagad;

    public PaymentGatewayResolver(
        IOptionsMonitor<PaymentOptions> options,
        MockGatewayLedger ledger,
        BkashGateway bkash,
        NagadGateway nagad)
    {
        _options = options;
        _ledger = ledger;
        _bkash = bkash;
        _nagad = nagad;
    }

    public IPaymentGateway Resolve(PaymentMethod method)
    {
        var o = _options.CurrentValue;
        return method switch
        {
            PaymentMethod.bKash => IsMock(o.Bkash.Mode) ? new MockPaymentGateway(method, _ledger) : _bkash,
            PaymentMethod.Nagad => IsMock(o.Nagad.Mode) ? new MockPaymentGateway(method, _ledger) : _nagad,
            _ => throw new ArgumentOutOfRangeException(nameof(method), "Wallet payments do not use a gateway.")
        };
    }

    private static bool IsMock(string mode) => !string.Equals(mode, GatewayModes.Live, StringComparison.OrdinalIgnoreCase);
}
