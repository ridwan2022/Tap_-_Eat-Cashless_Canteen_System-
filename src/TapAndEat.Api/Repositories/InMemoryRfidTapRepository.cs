using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public class InMemoryRfidTapRepository : IRfidTapRepository
{
    private readonly object _gate = new();
    private readonly List<RfidTapEvent> _taps = new();

    public Task AddAsync(RfidTapEvent tap)
    {
        lock (_gate)
        {
            _taps.Add(tap);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RfidTapEvent>> GetRecentAsync(int count)
    {
        lock (_gate)
        {
            IReadOnlyList<RfidTapEvent> result = _taps
                .OrderByDescending(t => t.TappedAtUtc)
                .Take(count)
                .ToList();
            return Task.FromResult(result);
        }
    }
}
