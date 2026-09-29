using System.Collections.Concurrent;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public class InMemoryUserRepository : IUserRepository
{
    // Registered as a singleton so the dictionary survives across requests
    // for the lifetime of the process (see Program.cs).
    private readonly ConcurrentDictionary<Guid, User> _users = new();

    public Task<User?> GetByIdAsync(Guid id)
    {
        _users.TryGetValue(id, out var user);
        return Task.FromResult(user);
    }

    public Task<User?> GetByEmailAsync(string email)
    {
        var match = _users.Values.FirstOrDefault(u =>
            string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match);
    }

    public Task<User> AddAsync(User user)
    {
        _users[user.Id] = user;
        return Task.FromResult(user);
    }

    public Task<IReadOnlyList<User>> GetAllAsync()
    {
        IReadOnlyList<User> all = _users.Values.OrderBy(u => u.CreatedAtUtc).ToList();
        return Task.FromResult(all);
    }

    public Task UpdateAsync(User user)
    {
        _users[user.Id] = user;
        return Task.CompletedTask;
    }

    public Task<bool> EmailExistsAsync(string email)
    {
        var exists = _users.Values.Any(u =>
            string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(exists);
    }
}
