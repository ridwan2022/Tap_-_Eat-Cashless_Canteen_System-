using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Services;

namespace TapAndEat.Api.Controllers;

[ApiController]
[Route("api/wallet")]
[Authorize]
public class WalletController : ControllerBase
{
    private readonly IWalletService _wallets;
    private readonly IPaymentService _payments;

    public WalletController(IWalletService wallets, IPaymentService payments)
    {
        _wallets = wallets;
        _payments = payments;
    }

    /// <summary>Task 5.2 — balance, linked RFID card, and subsidy schedule.</summary>
    [HttpGet]
    public async Task<ActionResult<WalletDto>> Get() => Ok(await _wallets.GetWalletAsync(this.CurrentUserId()));

    /// <summary>Task 5.5 — ledger, newest first.</summary>
    [HttpGet("transactions")]
    public async Task<ActionResult<IReadOnlyList<WalletTransactionDto>>> Transactions() =>
        Ok(await _wallets.GetTransactionsAsync(this.CurrentUserId()));

    /// <summary>
    /// Task 5.2 — top up through bKash or Nagad. Returns a payment with a redirect
    /// URL; the balance only changes once the gateway confirms. Non-positive
    /// amounts are rejected.
    /// </summary>
    [HttpPost("topup")]
    public async Task<ActionResult<PaymentDto>> TopUp(
        TopUpRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        if (!Enum.TryParse<PaymentMethod>(request.Method, ignoreCase: true, out var method) || !Enum.IsDefined(method))
        {
            return BadRequest(new { message = "Payment method must be bKash or Nagad." });
        }

        try
        {
            return Ok(await _payments.InitiateTopUpAsync(
                this.CurrentUserId(), request.Amount, method, idempotencyKey, this.BaseUrl(), ct));
        }
        catch (AppException ex) { return ex.ToActionResult(); }
    }
}
