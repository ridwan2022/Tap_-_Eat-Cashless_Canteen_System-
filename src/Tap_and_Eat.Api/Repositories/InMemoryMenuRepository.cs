using System.Collections.Concurrent;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public class InMemoryMenuRepository : IMenuRepository
{
    private readonly ConcurrentDictionary<Guid, MenuItem> _items = new();

    public Task<MenuItem?> GetByIdAsync(Guid id)
    {
        _items.TryGetValue(id, out var item);
        return Task.FromResult(item);
    }

    public Task<IReadOnlyList<MenuItem>> GetAllAsync()
    {
        IReadOnlyList<MenuItem> all = _items.Values.OrderBy(i => i.Category).ThenBy(i => i.Name).ToList();
        return Task.FromResult(all);
    }

    public Task<IReadOnlyList<MenuItem>> GetPublishedAsync(string? tag)
    {
        var query = _items.Values.Where(i => i.IsPublished);
        if (!string.IsNullOrWhiteSpace(tag))
        {
            query = query.Where(i => i.DietaryTags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)));
        }
        IReadOnlyList<MenuItem> result = query.OrderBy(i => i.Category).ThenBy(i => i.Name).ToList();
        return Task.FromResult(result);
    }

    public Task<MenuItem> AddAsync(MenuItem item)
    {
        _items[item.Id] = item;
        return Task.FromResult(item);
    }

    public Task UpdateAsync(MenuItem item)
    {
        item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        _items[item.Id] = item;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid id)
    {
        return Task.FromResult(_items.TryRemove(id, out _));
    }
}
