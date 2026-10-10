using TapAndEat.Api.Db;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories.Sql;

public class SqlNotificationRepository : INotificationRepository
{
    private readonly Database _db;

    public SqlNotificationRepository(Database db)
    {
        _db = db;
    }

    private static Notification MapNotification(DbRow r) => new()
    {
        Id = r.GetGuid("id"),
        UserId = r.GetGuid("user_id"),
        Type = r.GetString("type"),
        Channel = r.GetString("channel"),
        Title = r.GetString("title"),
        Message = r.GetString("message"),
        ReferenceId = r.GetStringOrNull("reference_id"),
        IsRead = r.GetBool("is_read"),
        CreatedAtUtc = r.GetDateTimeOffset("created_at"),
        ReadAtUtc = r.GetDateTimeOffsetOrNull("read_at")
    };

    private static PushSubscription MapSubscription(DbRow r) => new()
    {
        Id = r.GetGuid("id"),
        UserId = r.GetGuid("user_id"),
        Endpoint = r.GetString("endpoint"),
        P256dh = r.GetStringOrNull("p256dh"),
        Auth = r.GetStringOrNull("auth"),
        CreatedAtUtc = r.GetDateTimeOffset("created_at")
    };

    public async Task<Notification> AddAsync(Notification notification)
    {
        await _db.ExecuteAsync(
            """
            INSERT INTO notifications (id, user_id, type, channel, title, message, reference_id, is_read, created_at, read_at)
            VALUES (?,?,?,?,?,?,?,?,?,?)
            """,
            notification.Id, notification.UserId, notification.Type, notification.Channel, notification.Title,
            notification.Message, notification.ReferenceId, notification.IsRead, notification.CreatedAtUtc, notification.ReadAtUtc);
        return notification;
    }

    public async Task<Notification?> GetByIdAsync(Guid id) =>
        await _db.QuerySingleOrDefaultAsync("SELECT * FROM notifications WHERE id = ?", MapNotification, id);

    public async Task<IReadOnlyList<Notification>> GetByUserAsync(Guid userId, bool? unreadOnly = null, int limit = 50)
    {
        if (unreadOnly == true)
        {
            return await _db.QueryAsync(
                "SELECT * FROM notifications WHERE user_id = ? AND is_read = 0 ORDER BY created_at DESC LIMIT ?",
                MapNotification, userId, limit);
        }
        return await _db.QueryAsync(
            "SELECT * FROM notifications WHERE user_id = ? ORDER BY created_at DESC LIMIT ?",
            MapNotification, userId, limit);
    }

    public async Task<int> GetUnreadCountAsync(Guid userId)
    {
        var rows = await _db.QueryAsync(
            "SELECT COUNT(*) AS cnt FROM notifications WHERE user_id = ? AND is_read = 0",
            r => r.GetInt32("cnt"), userId);
        return rows.Count > 0 ? rows[0] : 0;
    }

    public async Task<bool> MarkAsReadAsync(Guid id, Guid userId, DateTimeOffset readAtUtc)
    {
        var affected = await _db.ExecuteAsync(
            "UPDATE notifications SET is_read = 1, read_at = ? WHERE id = ? AND user_id = ?",
            readAtUtc, id, userId);
        return affected > 0;
    }

    public async Task<int> MarkAllAsReadAsync(Guid userId, DateTimeOffset readAtUtc)
    {
        var affected = await _db.ExecuteAsync(
            "UPDATE notifications SET is_read = 1, read_at = ? WHERE user_id = ? AND is_read = 0",
            readAtUtc, userId);
        return affected;
    }

    public async Task AddPushSubscriptionAsync(PushSubscription subscription)
    {
        await _db.ExecuteAsync(
            """
            INSERT INTO push_subscriptions (id, user_id, endpoint, p256dh, auth, created_at)
            VALUES (?,?,?,?,?,?)
            ON CONFLICT(user_id, endpoint) DO UPDATE SET p256dh = excluded.p256dh, auth = excluded.auth
            """,
            subscription.Id, subscription.UserId, subscription.Endpoint, subscription.P256dh, subscription.Auth, subscription.CreatedAtUtc);
    }

    public async Task<IReadOnlyList<PushSubscription>> GetPushSubscriptionsAsync(Guid userId) =>
        await _db.QueryAsync("SELECT * FROM push_subscriptions WHERE user_id = ?", MapSubscription, userId);

    public Task RemovePushSubscriptionAsync(Guid userId, string endpoint) =>
        _db.ExecuteAsync("DELETE FROM push_subscriptions WHERE user_id = ? AND endpoint = ?", userId, endpoint);
}
