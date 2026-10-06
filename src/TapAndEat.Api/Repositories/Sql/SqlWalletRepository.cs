using TapAndEat.Api.Db;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories.Sql;

public class SqlWalletRepository : IWalletRepository
{
    private readonly Database _db;
    public SqlWalletRepository(Database db) { _db = db; }

    private static Wallet MapWallet(DbRow r) => new()
    {
        Id = r.GetGuid("id"),
        UserId = r.GetGuid("user_id"),
        Balance = r.GetDecimal("balance"),
        RfidCardUid = r.GetStringOrNull("rfid_card_uid"),
        CreatedAtUtc = r.GetDateTimeOffset("created_at")
    };

    private static WalletTransaction MapTx(DbRow r) => new()
    {
        Id = r.GetGuid("id"),
        WalletId = r.GetGuid("wallet_id"),
        Type = r.GetEnum<WalletTransactionType>("type"),
        Amount = r.GetDecimal("amount"),
        BalanceAfter = r.GetDecimal("balance_after"),
        Description = r.GetString("description"),
        IdempotencyKey = r.GetString("idempotency_key"),
        CreatedAtUtc = r.GetDateTimeOffset("created_at")
    };

    private static SubsidySchedule MapSchedule(DbRow r) => new()
    {
        Id = r.GetGuid("id"),
        UserId = r.GetGuid("user_id"),
        Amount = r.GetDecimal("amount"),
        Frequency = r.GetEnum<SubsidyFrequency>("frequency"),
        IsActive = r.GetBool("is_active"),
        LastCreditedPeriodKey = r.GetStringOrNull("last_credited_period_key")
    };

    public async Task<Wallet> GetOrCreateAsync(Guid userId) =>
        await _db.ExecuteInTransactionAsync(tx =>
        {
            var existing = tx.Query("SELECT * FROM wallets WHERE user_id = ?", MapWallet, userId);
            if (existing.Count > 0) return existing[0];

            var wallet = new Wallet { UserId = userId };
            tx.Execute("INSERT INTO wallets (id, user_id, balance, rfid_card_uid, created_at) VALUES (?,?,?,?,?)",
                wallet.Id, wallet.UserId, wallet.Balance, wallet.RfidCardUid, wallet.CreatedAtUtc);
            return wallet;
        });

    public async Task<Wallet?> GetByUserIdAsync(Guid userId) =>
        await _db.QuerySingleOrDefaultAsync("SELECT * FROM wallets WHERE user_id = ?", MapWallet, userId);

    public async Task<Wallet?> GetByCardUidAsync(string cardUid) =>
        await _db.QuerySingleOrDefaultAsync("SELECT * FROM wallets WHERE rfid_card_uid = ?", MapWallet, cardUid);

    public async Task<IReadOnlyList<Wallet>> GetAllAsync() =>
        await _db.QueryAsync("SELECT * FROM wallets ORDER BY created_at", MapWallet);

    public Task UpdateAsync(Wallet wallet) =>
        _db.ExecuteAsync("UPDATE wallets SET balance = ? WHERE id = ?", wallet.Balance, wallet.Id);

    public Task<bool> TryLinkCardAsync(Guid walletId, string cardUid) =>
        _db.ExecuteInTransactionAsync(tx =>
        {
            var owner = tx.Query("SELECT user_id FROM wallets WHERE rfid_card_uid = ?", r => r.GetGuid("user_id"), cardUid);
            var mine = tx.Query("SELECT id FROM wallets WHERE id = ? AND rfid_card_uid = ?", r => 1, walletId, cardUid);
            if (owner.Count > 0 && mine.Count == 0) return false; // held by a different wallet

            tx.Execute("UPDATE wallets SET rfid_card_uid = ? WHERE id = ?", cardUid, walletId);
            return true;
        });

    public Task UnlinkCardAsync(Guid walletId) =>
        _db.ExecuteAsync("UPDATE wallets SET rfid_card_uid = NULL WHERE id = ?", walletId);

    public async Task<WalletTransaction?> GetTransactionByKeyAsync(string idempotencyKey) =>
        await _db.QuerySingleOrDefaultAsync("SELECT * FROM wallet_transactions WHERE idempotency_key = ?", MapTx, idempotencyKey);

    public Task AddTransactionAsync(WalletTransaction transaction) => _db.ExecuteAsync(
        "INSERT INTO wallet_transactions (id, wallet_id, type, amount, balance_after, description, idempotency_key, created_at) VALUES (?,?,?,?,?,?,?,?)",
        transaction.Id, transaction.WalletId, transaction.Type.ToString(), transaction.Amount, transaction.BalanceAfter,
        transaction.Description, transaction.IdempotencyKey, transaction.CreatedAtUtc);

    public async Task<IReadOnlyList<WalletTransaction>> GetTransactionsAsync(Guid walletId) =>
        await _db.QueryAsync("SELECT * FROM wallet_transactions WHERE wallet_id = ? ORDER BY created_at DESC", MapTx, walletId);

    public async Task<SubsidySchedule?> GetScheduleAsync(Guid userId) =>
        await _db.QuerySingleOrDefaultAsync("SELECT * FROM subsidy_schedules WHERE user_id = ?", MapSchedule, userId);

    public Task UpsertScheduleAsync(SubsidySchedule schedule) => _db.ExecuteAsync(
        """
        INSERT INTO subsidy_schedules (id, user_id, amount, frequency, is_active, last_credited_period_key) VALUES (?,?,?,?,?,?)
        ON CONFLICT(user_id) DO UPDATE SET amount=excluded.amount, frequency=excluded.frequency,
            is_active=excluded.is_active, last_credited_period_key=excluded.last_credited_period_key
        """,
        schedule.Id, schedule.UserId, schedule.Amount, schedule.Frequency.ToString(), schedule.IsActive, schedule.LastCreditedPeriodKey);

    public async Task<IReadOnlyList<SubsidySchedule>> GetActiveSchedulesAsync() =>
        await _db.QueryAsync("SELECT * FROM subsidy_schedules WHERE is_active = 1", MapSchedule);
}
