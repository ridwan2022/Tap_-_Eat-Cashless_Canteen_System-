using System.Collections.Concurrent;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories;

public class InMemoryWalletRepository : IWalletRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Wallet> _wallets = new();            // by wallet id
    private readonly Dictionary<Guid, Guid> _walletByUser = new();
    private readonly Dictionary<string, Guid> _walletByCard = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WalletTransaction> _txByKey = new();
    private readonly List<WalletTransaction> _transactions = new();
    private readonly Dictionary<Guid, SubsidySchedule> _schedules = new(); // by user id

    public Task<Wallet> GetOrCreateAsync(Guid userId)
    {
        lock (_gate)
        {
            if (_walletByUser.TryGetValue(userId, out var id))
            {
                return Task.FromResult(_wallets[id]);
            }

            var wallet = new Wallet { UserId = userId };
            _wallets[wallet.Id] = wallet;
            _walletByUser[userId] = wallet.Id;
            return Task.FromResult(wallet);
        }
    }

    public Task<Wallet?> GetByUserIdAsync(Guid userId)
    {
        lock (_gate)
        {
            return Task.FromResult(_walletByUser.TryGetValue(userId, out var id) ? _wallets[id] : null);
        }
    }

    public Task<Wallet?> GetByCardUidAsync(string cardUid)
    {
        lock (_gate)
        {
            return Task.FromResult(_walletByCard.TryGetValue(cardUid, out var id) ? _wallets[id] : null);
        }
    }

    public Task<IReadOnlyList<Wallet>> GetAllAsync()
    {
        lock (_gate)
        {
            IReadOnlyList<Wallet> all = _wallets.Values.OrderBy(w => w.CreatedAtUtc).ToList();
            return Task.FromResult(all);
        }
    }

    public Task UpdateAsync(Wallet wallet)
    {
        lock (_gate)
        {
            _wallets[wallet.Id] = wallet;
        }
        return Task.CompletedTask;
    }

    public Task<bool> TryLinkCardAsync(Guid walletId, string cardUid)
    {
        lock (_gate)
        {
            if (_walletByCard.TryGetValue(cardUid, out var owner) && owner != walletId)
            {
                return Task.FromResult(false);
            }

            var wallet = _wallets[walletId];
            if (wallet.RfidCardUid is not null)
            {
                _walletByCard.Remove(wallet.RfidCardUid);
            }
            wallet.RfidCardUid = cardUid;
            _walletByCard[cardUid] = walletId;
            return Task.FromResult(true);
        }
    }

    public Task UnlinkCardAsync(Guid walletId)
    {
        lock (_gate)
        {
            var wallet = _wallets[walletId];
            if (wallet.RfidCardUid is not null)
            {
                _walletByCard.Remove(wallet.RfidCardUid);
                wallet.RfidCardUid = null;
            }
        }
        return Task.CompletedTask;
    }

    public Task<WalletTransaction?> GetTransactionByKeyAsync(string idempotencyKey)
    {
        lock (_gate)
        {
            _txByKey.TryGetValue(idempotencyKey, out var tx);
            return Task.FromResult(tx);
        }
    }

    public Task AddTransactionAsync(WalletTransaction transaction)
    {
        lock (_gate)
        {
            if (!_txByKey.TryAdd(transaction.IdempotencyKey, transaction))
            {
                throw new InvalidOperationException(
                    $"A wallet transaction with idempotency key '{transaction.IdempotencyKey}' already exists.");
            }
            _transactions.Add(transaction);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WalletTransaction>> GetTransactionsAsync(Guid walletId)
    {
        lock (_gate)
        {
            IReadOnlyList<WalletTransaction> result = _transactions
                .Where(t => t.WalletId == walletId)
                .OrderByDescending(t => t.CreatedAtUtc)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<SubsidySchedule?> GetScheduleAsync(Guid userId)
    {
        lock (_gate)
        {
            _schedules.TryGetValue(userId, out var schedule);
            return Task.FromResult(schedule);
        }
    }

    public Task UpsertScheduleAsync(SubsidySchedule schedule)
    {
        lock (_gate)
        {
            _schedules[schedule.UserId] = schedule;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SubsidySchedule>> GetActiveSchedulesAsync()
    {
        lock (_gate)
        {
            IReadOnlyList<SubsidySchedule> result = _schedules.Values.Where(s => s.IsActive).ToList();
            return Task.FromResult(result);
        }
    }
}
