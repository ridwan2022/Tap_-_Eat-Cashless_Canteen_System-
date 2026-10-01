using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Services;

namespace TapAndEat.Api.Controllers;

/// <summary>
/// Tasks 6.2–6.3 — the meal-collection counter. Reachable only by kitchen
/// staff/admins; a rejected tap is a normal, successful HTTP response whose
/// body says <c>Outcome: Rejected</c> and why.
/// </summary>
[ApiController]
[Route("api/counter")]
[Authorize(Roles = Roles.Staff)]
public class CounterController : ControllerBase
{
    private readonly ICounterService _counter;

    public CounterController(ICounterService counter)
    {
        _counter = counter;
    }

    [HttpPost("tap")]
    public async Task<ActionResult<TapResultDto>> Tap(TapRequest request) =>
        Ok(await _counter.TapAsync(request.CardUid, this.CurrentUserId()));

    [HttpPost("verify")]
    public async Task<ActionResult<TapResultDto>> Verify(ManualVerifyRequest request) =>
        Ok(await _counter.VerifyManuallyAsync(request, this.CurrentUserId()));

    [HttpPost("tap-to-pay")]
    public async Task<ActionResult<TapResultDto>> TapToPay(TapToPayRequest request) =>
        Ok(await _counter.TapToPayAsync(request.CardUid, request.OrderId, this.CurrentUserId()));

    [HttpGet("recent")]
    public async Task<ActionResult<IReadOnlyList<TapResultDto>>> Recent([FromQuery] int count = 20) =>
        Ok(await _counter.GetRecentAsync(count));
}
