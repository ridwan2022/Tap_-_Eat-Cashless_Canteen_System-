using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Services;

namespace TapAndEat.Api.Controllers;

[ApiController]
[Route("api/queue")]
public class QueueController : ControllerBase
{
    private readonly IQueuePositionService _position;

    public QueueController(IQueuePositionService position)
    {
        _position = position;
    }

    /// <summary>Task 8.2 — the caller's live position for their own order.</summary>
    [HttpGet("position/{orderId:guid}")]
    [Authorize]
    public async Task<ActionResult<QueuePositionDto>> MyPosition(Guid orderId)
    {
        try { return Ok(await _position.GetMyPositionAsync(orderId, this.CurrentUserId())); }
        catch (AppException ex) { return ex.ToActionResult(); }
    }

    /// <summary>
    /// Task 8.3 — the public token display board: active token numbers and
    /// status only (no names, no items). Anonymous on purpose — it's meant
    /// to run on a TV screen in the dining area.
    /// </summary>
    [HttpGet("board")]
    [AllowAnonymous]
    public async Task<ActionResult<TokenBoardDto>> Board() => Ok(await _position.GetBoardAsync());
}
