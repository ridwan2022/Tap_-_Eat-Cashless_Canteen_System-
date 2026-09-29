using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using TapAndEat.Api.Services;

namespace TapAndEat.Api.Auth;

public static class AuthSchemes
{
    public const string Bearer = "TapAndEatBearer";
}

public class BearerTokenAuthenticationOptions : AuthenticationSchemeOptions
{
}

/// <summary>
/// Validates the "Authorization: Bearer &lt;token&gt;" header against
/// <see cref="ITokenService"/> and, on success, builds a ClaimsPrincipal
/// carrying the user's id and role so that [Authorize(Roles = "Admin")]
/// works the same way it would with JWT bearer auth (Task 1.3, RBAC).
/// </summary>
public class BearerTokenAuthenticationHandler : AuthenticationHandler<BearerTokenAuthenticationOptions>
{
    private readonly ITokenService _tokenService;

    public BearerTokenAuthenticationHandler(
        IOptionsMonitor<BearerTokenAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ITokenService tokenService)
        : base(options, logger, encoder)
    {
        _tokenService = tokenService;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var headerValue = authHeader.ToString();
        const string prefix = "Bearer ";
        if (!headerValue.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = headerValue[prefix.Length..].Trim();
        var info = _tokenService.ValidateToken(token);
        if (info is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing, expired, or invalid token."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, info.UserId.ToString()),
            new Claim(ClaimTypes.Role, info.Role.ToString()),
            new Claim("security_stamp", info.SecurityStamp.ToString())
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Response.WriteAsJsonAsync(new { message = "Missing/expired/invalid token." });
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Response.WriteAsJsonAsync(new { message = "You do not have permission to perform this action." });
    }
}
