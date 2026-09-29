using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public class InMemoryQueueTokenRepository : IQueueTokenRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, QueueToken> _tokens = new();
    private readonly Dictionary<Guid, Guid> _tokenByOrder = new();
    private readonly Dictionary<string, int> _lastNumberByDay = new();

    public Task<QueueToken> AddForOrderAsync(Guid orderId, string dayKey, Func<int, QueueToken> factory)
    {
        lock (_gate)
        {
            if (_tokenByOrder.TryGetValue(orderId, out var existingId))
            {
                return Task.FromResult(_tokens[existingId]);
            }

            _lastNumberByDay.TryGetValue(dayKey, out var last);
            var number = last + 1;
            var token = factory(number);
            _lastNumberByDay[dayKey] = number;
            _tokens[token.Id] = token;
            _tokenByOrder[orderId] = token.Id;
            return Task.FromResult(token);
        }
    }

    public Task<QueueToken?> GetByIdAsync(Guid id)
    {
        lock (_gate)
        {
            _tokens.TryGetValue(id, out var token);
            return Task.FromResult(token);
        }
    }

    public Task<QueueToken?> GetByOrderIdAsync(Guid orderId)
    {
        lock (_gate)
        {
            return Task.FromResult(_tokenByOrder.TryGetValue(orderId, out var id) ? _tokens[id] : null);
        }
    }

    public Task<QueueToken?> GetByNumberAsync(string dayKey, int tokenNumber)
    {
        lock (_gate)
        {
            var match = _tokens.Values.FirstOrDefault(t => t.DayKey == dayKey && t.TokenNumber == tokenNumber);
            return Task.FromResult(match);
        }
    }

    public Task<IReadOnlyList<QueueToken>> GetActiveAsync()
    {
        lock (_gate)
        {
            IReadOnlyList<QueueToken> result = _tokens.Values
                .Where(t => t.Status != QueueTokenStatus.Collected)
                .OrderBy(t => t.CreatedAtUtc)
                .ThenBy(t => t.TokenNumber)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task UpdateAsync(QueueToken token)
    {
        lock (_gate)
        {
            _tokens[token.Id] = token;
        }
        return Task.CompletedTask;
    }
}

