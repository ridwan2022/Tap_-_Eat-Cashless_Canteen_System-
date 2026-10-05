using TapAndEat.Api.Db;
using TapAndEat.Api.Models;

namespace TapAndEat.Api.Repositories.Sql;

public class SqlUserRepository : IUserRepository
{
    private readonly Database _db;
    public SqlUserRepository(Database db) { _db = db; }

    private static User Map(DbRow r) => new()
    {
        Id = r.GetGuid("id"),
        FullName = r.GetString("full_name"),
        Email = r.GetString("email"),
        PasswordHash = r.GetString("password_hash"),
        Role = r.GetEnum<UserRole>("role"),
        IsActive = r.GetBool("is_active"),
        SecurityStamp = r.GetInt32("security_stamp"),
        CreatedAtUtc = r.GetDateTimeOffset("created_at")
    };

    public async Task<User?> GetByIdAsync(Guid id) =>
        await _db.QuerySingleOrDefaultAsync("SELECT * FROM users WHERE id = ?", Map, id);

    public async Task<User?> GetByEmailAsync(string email) =>
        await _db.QuerySingleOrDefaultAsync("SELECT * FROM users WHERE email = ? COLLATE NOCASE", Map, email);

    public async Task<User> AddAsync(User user)
    {
        await _db.ExecuteAsync(
            "INSERT INTO users (id, full_name, email, password_hash, role, is_active, security_stamp, created_at) VALUES (?,?,?,?,?,?,?,?)",
            user.Id, user.FullName, user.Email, user.PasswordHash, user.Role.ToString(), user.IsActive, user.SecurityStamp, user.CreatedAtUtc);
        return user;
    }

    public async Task<IReadOnlyList<User>> GetAllAsync() =>
        await _db.QueryAsync("SELECT * FROM users ORDER BY created_at", Map);

    public Task UpdateAsync(User user) => _db.ExecuteAsync(
        "UPDATE users SET full_name=?, email=?, password_hash=?, role=?, is_active=?, security_stamp=? WHERE id=?",
        user.FullName, user.Email, user.PasswordHash, user.Role.ToString(), user.IsActive, user.SecurityStamp, user.Id);

    public async Task<bool> EmailExistsAsync(string email) =>
        (await _db.QueryAsync("SELECT 1 c FROM users WHERE email = ? COLLATE NOCASE", r => 1, email)).Count > 0;
}
