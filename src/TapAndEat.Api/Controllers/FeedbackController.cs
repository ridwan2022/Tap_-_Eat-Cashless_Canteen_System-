using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Services;

namespace TapAndEat.Api.Controllers;

[ApiController]
[Route("api/feedback")]
public class FeedbackController : ControllerBase
{
    private readonly IFeedbackService _feedback;

    public FeedbackController(IFeedbackService feedback)
    {
        _feedback = feedback;
    }

    /// <summary>Task 13.2 — Submit meal satisfaction rating and comments.</summary>
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Customer))]
    public async Task<ActionResult<OrderFeedbackDto>> Submit(CreateFeedbackRequest request)
    {
        var userName = User.Identity?.Name ?? "Customer";
        try
        {
            var result = await _feedback.SubmitFeedbackAsync(this.CurrentUserId(), userName, request);
            return Ok(result);
        }
        catch (AppException ex) { return ex.ToActionResult(); }
    }

    /// <summary>Task 13.2 — Get feedback for a specific completed order.</summary>
    [HttpGet("order/{orderId:guid}")]
    [Authorize]
    public async Task<ActionResult<OrderFeedbackDto>> GetByOrder(Guid orderId)
    {
        try
        {
            var result = await _feedback.GetFeedbackByOrderIdAsync(orderId, this.CurrentUserId());
            return result is not null ? Ok(result) : NotFound(new { message = "No feedback for this order yet." });
        }
        catch (AppException ex) { return ex.ToActionResult(); }
    }

    /// <summary>Task 13.2 — Get all feedbacks submitted by caller.</summary>
    [HttpGet("mine")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<OrderFeedbackDto>>> Mine() =>
        Ok(await _feedback.GetMyFeedbacksAsync(this.CurrentUserId()));

    /// <summary>Task 13.4 — Admin satisfaction summary and dish ratings dashboard.</summary>
    [HttpGet("summary")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<FeedbackSummaryDto>> Summary() =>
        Ok(await _feedback.GetSummaryAsync());
}
