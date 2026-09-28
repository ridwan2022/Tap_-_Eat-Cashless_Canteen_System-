using TapAndEat.Api.Models;

namespace TapAndEat.Api.Services;

public record IssuedToken(string Token, DateTimeOffset ExpiresAtUtc);

public record TokenInfo(Guid UserId, UserRole Role, int SecurityStamp, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Task 1.3 — session/token-based authentication. Sprint 1 issues opaque,
/// server-tracked bearer tokens (a GUID) rather than JWTs so the project
/// needs no external cryptography/JWT NuGet package to run. Swapping this
/// for a JWT implementation later only requires re-implementing this one
/// interface — see README.md "Sprint 2 upgrade path".
/// </summary>
public interface ITokenService
{
    IssuedToken IssueToken(User user);

    /// <summary>Returns null when the token is missing, expired, or was revoked.</summary>
    TokenInfo? ValidateToken(string token);

    void RevokeToken(string token);

    /// <summary>Revokes every token currently issued to a user (e.g. after a password reset).</summary>
    void RevokeAllForUser(Guid userId);
}
