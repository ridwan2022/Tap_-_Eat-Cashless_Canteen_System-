using System.Collections.Concurrent;
using System.Security.Cryptography;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Services;

/// <summary>
/// Registered as a singleton (see Program.cs) so issued tokens survive for
/// the lifetime of the process, matching how a distributed cache (Redis) or
/// a database-backed token table would behave in Sprint 2.
/// </summary>
public class TokenService : ITokenService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(8);
    private readonly ConcurrentDictionary<string, TokenInfo> _tokens = new();

    public IssuedToken IssueToken(User user)
    {
        var token = GenerateOpaqueToken();
        var expiresAtUtc = DateTimeOffset.UtcNow.Add(TokenLifetime);
        _tokens[token] = new TokenInfo(user.Id, user.Role, user.SecurityStamp, expiresAtUtc);
        return new IssuedToken(token, expiresAtUtc);
    }

    public TokenInfo? ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        if (!_tokens.TryGetValue(token, out var info))
        {
            return null;
        }

        if (info.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            _tokens.TryRemove(token, out _);
            return null;
        }

        return info;
    }

    public void RevokeToken(string token) => _tokens.TryRemove(token, out _);

    public void RevokeAllForUser(Guid userId)
    {
        foreach (var kvp in _tokens)
        {
            if (kvp.Value.UserId == userId)
            {
                _tokens.TryRemove(kvp.Key, out _);
            }
        }
    }

    private static string GenerateOpaqueToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
