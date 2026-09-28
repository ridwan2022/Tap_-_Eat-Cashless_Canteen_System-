using System.Collections.Concurrent;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public class InMemoryPasswordResetRepository : IPasswordResetRepository
{
    private readonly ConcurrentDictionary<Guid, PasswordResetToken> _tokens = new();

    public Task<PasswordResetToken> AddAsync(PasswordResetToken token)
    {
        _tokens[token.Id] = token;
        return Task.FromResult(token);
    }

    public Task<PasswordResetToken?> GetLatestValidForUserAsync(Guid userId, string tokenHash)
    {
        var match = _tokens.Values
            .Where(t => t.UserId == userId && t.TokenHash == tokenHash && !t.Used
                        && t.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .OrderByDescending(t => t.ExpiresAtUtc)
            .FirstOrDefault();
        return Task.FromResult(match);
    }

    public Task MarkUsedAsync(Guid id)
    {
        if (_tokens.TryGetValue(id, out var token))
        {
            token.Used = true;
        }
        return Task.CompletedTask;
    }
}
