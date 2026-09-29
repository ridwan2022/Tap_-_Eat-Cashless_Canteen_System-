using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public interface IMenuRepository
{
    Task<MenuItem?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<MenuItem>> GetAllAsync();
    Task<IReadOnlyList<MenuItem>> GetPublishedAsync(string? tag);
    Task<MenuItem> AddAsync(MenuItem item);
    Task UpdateAsync(MenuItem item);
    Task<bool> DeleteAsync(Guid id);
}
