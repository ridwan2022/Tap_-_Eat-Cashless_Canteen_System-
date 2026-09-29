using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Services;

namespace TapAndEat.Api.Controllers;

/// <summary>
/// Admin-only wallet operations. Cards are issued by an administrator (rather
/// than typed in by the employee) so nobody can attach a card they don't
/// hold — an RFID UID is easy to read off but hard to justify owning.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class AdminWalletsController : ControllerBase
{
    private readonly IWalletService _wallets;
    private readonly ISubsidyService _subsidies;

    public AdminWalletsController(IWalletService wallets, ISubsidyService subsidies)
    {
        _wallets = wallets;
        _subsidies = subsidies;
    }

    [HttpGet("wallets")]
    public async Task<ActionResult<IReadOnlyList<AdminWalletDto>>> List() => Ok(await _wallets.GetAllForAdminAsync());

    /// <summary>Task 5.3 — link an RFID card to a user's wallet (one card ↔ one wallet).</summary>
    [HttpPut("wallets/{userId:guid}/card")]
    public async Task<ActionResult<WalletDto>> LinkCard(Guid userId, LinkCardRequest request)
    {
        try { return Ok(await _wallets.LinkCardAsync(userId, request.CardUid)); }
        catch (AppException ex) { return ex.ToActionResult(); }
    }

    [HttpDelete("wallets/{userId:guid}/card")]
    public async Task<ActionResult<WalletDto>> UnlinkCard(Guid userId)
    {
        try { return Ok(await _wallets.UnlinkCardAsync(userId)); }
        catch (AppException ex) { return ex.ToActionResult(); }
    }

    /// <summary>Task 5.1 — configure the subsidy schedule for an employee.</summary>
    [HttpPut("wallets/{userId:guid}/subsidy")]
    public async Task<ActionResult<SubsidyScheduleDto>> SetSubsidy(Guid userId, SetSubsidyRequest request)
    {
        if (!Enum.TryParse<SubsidyFrequency>(request.Frequency, ignoreCase: true, out var frequency) || !Enum.IsDefined(frequency))
        {
            return BadRequest(new { message = "Frequency must be Daily, Weekly or Monthly." });
        }

        try { return Ok(await _subsidies.SetScheduleAsync(userId, request.Amount, frequency, request.IsActive)); }
        catch (AppException ex) { return ex.ToActionResult(); }
    }

    /// <summary>Task 5.4 — run the subsidy job now (the background scheduler does this automatically). Idempotent.</summary>
    [HttpPost("subsidies/run")]
    public async Task<ActionResult<SubsidyRunResultDto>> RunSubsidies() => Ok(await _subsidies.RunDueCreditsAsync());
}
