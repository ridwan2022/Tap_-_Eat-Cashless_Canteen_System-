using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

/// <summary>
/// Sprint 1 backs this with an in-memory store (see
/// InMemoryUserRepository). The interface is written the way a
/// DbSet&lt;User&gt;-backed EF Core repository would look, so Sprint 2 can
/// swap the implementation for one backed by SQL Server/Postgres without
/// touching any service or controller code.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id);
    Task<User?> GetByEmailAsync(string email);
    Task<User> AddAsync(User user);
    Task<IReadOnlyList<User>> GetAllAsync();
    Task UpdateAsync(User user);
    Task<bool> EmailExistsAsync(string email);
}
