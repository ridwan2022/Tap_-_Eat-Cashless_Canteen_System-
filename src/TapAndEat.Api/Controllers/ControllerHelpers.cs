using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace TapAndEat.Api.Controllers;

public static class Roles
{
    /// <summary>Roles allowed to operate the kitchen board and the counter.</summary>
    public const string Staff = "KitchenStaff,Admin";
}

public static class ControllerHelpers
{
    public static Guid CurrentUserId(this ControllerBase controller) =>
        Guid.Parse(controller.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static bool IsStaff(this ControllerBase controller) =>
        controller.User.IsInRole("KitchenStaff") || controller.User.IsInRole("Admin");

    /// <summary>scheme://host of the incoming request — where gateways send the customer back to.</summary>
    public static string BaseUrl(this ControllerBase controller) =>
        $"{controller.Request.Scheme}://{controller.Request.Host}";
}
