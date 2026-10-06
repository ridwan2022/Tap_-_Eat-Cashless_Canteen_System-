using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Services;

namespace TapAndEat.Api.Controllers;

/// <summary>Tasks 9.3/9.4 — next-day demand forecast API for the admin dashboard.</summary>
[ApiController]
[Route("api/admin/forecast")]
[Authorize(Roles = nameof(Models.UserRole.Admin))]
public class ForecastController : ControllerBase
{
    private readonly IForecastService _forecasts;

    public ForecastController(IForecastService forecasts)
    {
        _forecasts = forecasts;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ForecastDto>>> All() => Ok(await _forecasts.GetAllForecastsAsync());

    [HttpGet("{menuItemId:guid}")]
    public async Task<ActionResult<ForecastDto>> One(Guid menuItemId)
    {
        try { return Ok(await _forecasts.GetForecastAsync(menuItemId)); }
        catch (AppException ex) { return ex.ToActionResult(); }
    }
}
