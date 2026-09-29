using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Services;

namespace TapAndEat.Api.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _payments;

    public PaymentsController(IPaymentService payments)
    {
        _payments = payments;
    }

    /// <summary>
    /// Pay for an order with bKash, Nagad or the wallet. Send an
    /// <c>Idempotency-Key</c> header (any unique string per checkout attempt):
    /// repeating the request with the same key returns the original result
    /// instead of charging twice.
    /// </summary>
    [HttpPost("order")]
    [Authorize]
    public async Task<ActionResult<PaymentDto>> PayOrder(
        PayOrderRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        if (!TryParseMethod(request.Method, out var method))
        {
            return BadRequest(new { message = "Payment method must be bKash, Nagad or Wallet." });
        }

        try
        {
            return Ok(await _payments.InitiateOrderPaymentAsync(
                this.CurrentUserId(), request.OrderId, method, idempotencyKey, this.BaseUrl(), ct));
        }
        catch (AppException ex) { return ex.ToActionResult(); }
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<PaymentDto>> Get(Guid id)
    {
        try { return Ok(await _payments.GetPaymentAsync(this.CurrentUserId(), id)); }
        catch (AppException ex) { return ex.ToActionResult(); }
    }

    /// <summary>Re-checks an in-flight payment with the gateway (use after a timeout).</summary>
    [HttpPost("{id:guid}/verify")]
    [Authorize]
    public async Task<ActionResult<PaymentDto>> Verify(Guid id, CancellationToken ct)
    {
        try { return Ok(await _payments.VerifyPaymentAsync(this.CurrentUserId(), id, ct)); }
        catch (AppException ex) { return ex.ToActionResult(); }
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize]
    public async Task<ActionResult<PaymentDto>> Cancel(Guid id)
    {
        try { return Ok(await _payments.CancelPaymentAsync(this.CurrentUserId(), id)); }
        catch (AppException ex) { return ex.ToActionResult(); }
    }

    /// <summary>
    /// Where bKash/Nagad send the customer's browser back to. Anonymous by
    /// necessity (a redirect can't carry our bearer token) — which is why
    /// nothing in the query string is trusted: the payment service confirms
    /// with the gateway server-to-server before marking anything paid.
    /// </summary>
    [HttpGet("callback/{method}")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback(string method, [FromQuery] Guid pid, CancellationToken ct)
    {
        if (!TryParseMethod(method, out var parsed) || parsed == PaymentMethod.Wallet)
        {
            return Redirect("/payment-result.html?error=unknown-method");
        }

        var query = Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        try
        {
            await _payments.HandleCallbackAsync(parsed, pid, query, ct);
            return Redirect($"/payment-result.html?paymentId={pid}");
        }
        catch (AppException)
        {
            return Redirect("/payment-result.html?error=not-found");
        }
    }

    private static bool TryParseMethod(string? value, out PaymentMethod method) =>
        Enum.TryParse(value, ignoreCase: true, out method) && Enum.IsDefined(method);
}
