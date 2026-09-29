using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Payments;

namespace TapAndEat.Api.Controllers;

public record MockDecisionRequest(string Outcome);

/// <summary>
/// Backs the fake bKash/Nagad checkout page (wwwroot/mock-gateway.html) used
/// while a gateway runs in Mock mode. Returns 404 for everything once both
/// gateways are switched to Live, so this can't be used to fake a payment
/// against a real gateway.
/// </summary>
[ApiController]
[Route("api/mock-gateway")]
[AllowAnonymous]
public class MockGatewayController : ControllerBase
{
    private readonly MockGatewayLedger _ledger;
    private readonly IOptionsMonitor<PaymentOptions> _options;

    public MockGatewayController(MockGatewayLedger ledger, IOptionsMonitor<PaymentOptions> options)
    {
        _ledger = ledger;
        _options = options;
    }

    private bool Enabled =>
        !string.Equals(_options.CurrentValue.Bkash.Mode, GatewayModes.Live, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(_options.CurrentValue.Nagad.Mode, GatewayModes.Live, StringComparison.OrdinalIgnoreCase);

    [HttpGet("{gatewayRef}")]
    public IActionResult Get(string gatewayRef)
    {
        var payment = Enabled ? _ledger.Get(gatewayRef) : null;
        return payment is null
            ? NotFound()
            : Ok(new { method = payment.Method.ToString(), amount = payment.Amount, decided = payment.Outcome });
    }

    /// <summary>The "customer" presses Pay / Fail / Cancel on the fake checkout page.</summary>
    [HttpPost("{gatewayRef}/decide")]
    public IActionResult Decide(string gatewayRef, MockDecisionRequest request)
    {
        var payment = Enabled ? _ledger.Get(gatewayRef) : null;
        if (payment is null) return NotFound();

        var outcome = request.Outcome?.ToLowerInvariant();
        if (outcome is not ("success" or "failed" or "cancelled"))
        {
            return BadRequest(new { message = "Outcome must be success, failed or cancelled." });
        }

        _ledger.Decide(gatewayRef, outcome); // first decision wins
        var status = payment.Outcome switch { "success" => "success", "cancelled" => "cancel", _ => "failure" };
        var separator = payment.CallbackUrl.Contains('?') ? '&' : '?';
        return Ok(new { redirectUrl = $"{payment.CallbackUrl}{separator}status={status}" });
    }
}
