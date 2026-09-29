using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Services;

namespace TapAndEat.Api.Controllers;

[ApiController]
[Route("api/kitchen")]
[Authorize(Roles = Roles.Staff)]
public class KitchenController : ControllerBase
{
    private readonly IQueueService _queue;

    public KitchenController(IQueueService queue)
    {
        _queue = queue;
    }

    /// <summary>Task 7.3 — active tokens in queue order with live prep-time estimates.</summary>
    [HttpGet("board")]
    public async Task<ActionResult<KitchenBoardDto>> Board() => Ok(await _queue.GetBoardAsync());

    /// <summary>Move a token forward: Preparing, then Ready.</summary>
    [HttpPut("tokens/{id:guid}/status")]
    public async Task<ActionResult<QueueTokenDto>> SetStatus(Guid id, SetTokenStatusRequest request)
    {
        if (!Enum.TryParse<QueueTokenStatus>(request.Status, ignoreCase: true, out var status) || !Enum.IsDefined(status))
        {
            return BadRequest(new { message = "Status must be Preparing or Ready." });
        }

        try { return Ok(await _queue.SetStatusAsync(id, status)); }
        catch (AppException ex) { return ex.ToActionResult(); }
    }
}
