using System.Globalization;
using Microsoft.Extensions.Options;
using static TapAndEat.Api.Db.Sqlite3Native;

namespace TapAndEat.Api.Db;

public class DatabaseOptions
{
    /// <summary>Path to the SQLite file. Relative paths are under the app's content root.</summary>
    public string Path { get; set; } = "App_Data/tapandeat.db";
}

/// <summary>A single bound parameter for a prepared statement, in ? order.</summary>
public readonly struct DbParam
{
    public readonly object? Value;
    private DbParam(object? value) { Value = value; }
    public static DbParam Of(object? value) => new(value);
    public static implicit operator DbParam(string? v) => new(v);
    public static implicit operator DbParam(Guid v) => new(v.ToString());
    public static implicit operator DbParam(Guid? v) => new(v?.ToString());
    public static implicit operator DbParam(bool v) => new(v ? 1L : 0L);
    public static implicit operator DbParam(int v) => new((long)v);
    public static implicit operator DbParam(int? v) => new((long?)v);
    public static implicit operator DbParam(long v) => new(v);
    public static implicit operator DbParam(decimal v) => new(v.ToString(CultureInfo.InvariantCulture));
    public static implicit operator DbParam(decimal? v) => new(v?.ToString(CultureInfo.InvariantCulture));
    public static implicit operator DbParam(double v) => new(v);
    public static implicit operator DbParam(DateTimeOffset v) => new(v.UtcTicks);
    public static implicit operator DbParam(DateTimeOffset? v) => new(v?.UtcTicks);
}

/// <summary>Thin reader over one result row — enough for the mapping code our repositories need.</summary>
public sealed class DbRow
{
    private readonly Dictionary<string, (int Type, IntPtr TextPtr, int TextLen, long Int, double Real)> _cols = new();

    internal DbRow(IntPtr stmt)
    {
        var n = sqlite3_column_count(stmt);
        for (var i = 0; i < n; i++)
        {
            // sqlite3_column_name returns a NUL-terminated UTF-8 string (no separate length call needed).
            var name = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(sqlite3_column_name(stmt, i))
                ?? throw new InvalidOperationException("Could not read column name.");
            var type = sqlite3_column_type(stmt, i);
            _cols[name] = type switch
            {
                SQLITE_TEXT => (type, sqlite3_column_text(stmt, i), sqlite3_column_bytes(stmt, i), 0, 0),
                SQLITE_INTEGER => (type, IntPtr.Zero, 0, sqlite3_column_int64(stmt, i), 0),
                SQLITE_FLOAT => (type, IntPtr.Zero, 0, 0, sqlite3_column_double(stmt, i)),
                _ => (type, IntPtr.Zero, 0, 0, 0)
            };
        }
    }

    private (int Type, IntPtr TextPtr, int TextLen, long Int, double Real) Col(string name) =>
        _cols.TryGetValue(name, out var v) ? v : throw new InvalidOperationException($"No column '{name}' in result.");

    public bool IsNull(string name) => Col(name).Type == SQLITE_NULL;
    public string GetString(string name) => Utf8PtrToString(Col(name).TextPtr, Col(name).TextLen) ?? string.Empty;
    public string? GetStringOrNull(string name) => IsNull(name) ? null : GetString(name);
    public Guid GetGuid(string name) => Guid.Parse(GetString(name));
    public Guid? GetGuidOrNull(string name) => IsNull(name) ? null : Guid.Parse(GetString(name));
    public long GetInt64(string name) => Col(name).Int;
    public int GetInt32(string name) => (int)Col(name).Int;
    public bool GetBool(string name) => Col(name).Int != 0;
    public bool GetBoolOrFalse(string name) => !IsNull(name) && Col(name).Int != 0;
    public decimal GetDecimal(string name) => decimal.Parse(GetString(name), CultureInfo.InvariantCulture);
    public decimal? GetDecimalOrNull(string name) => IsNull(name) ? null : GetDecimal(name);
    public DateTimeOffset GetDateTimeOffset(string name) => new(Col(name).Int, TimeSpan.Zero);
    public DateTimeOffset? GetDateTimeOffsetOrNull(string name) => IsNull(name) ? null : GetDateTimeOffset(name);
    public TEnum GetEnum<TEnum>(string name) where TEnum : struct, Enum => Enum.Parse<TEnum>(GetString(name));
}

/// <summary>
/// The whole database access layer: one connection, one writer lock (SQLite
/// allows many readers or one writer — we simplify to "one operation at a
/// time", which is plenty for this app's scale and keeps every write
/// trivially atomic), WAL journaling so a crash mid-write can't corrupt the
/// file, and a tiny prepared-statement helper. Every repository in
/// Repositories/ goes through this — nothing talks to libsqlite3 directly.
/// </summary>
public sealed class Database : IDisposable
{
    private readonly IntPtr _db;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string FilePath { get; }

    public Database(IOptions<DatabaseOptions> options, string? contentRoot = null)
    {
        var path = options.Value.Path;
        if (!System.IO.Path.IsPathRooted(path))
        {
            path = System.IO.Path.Combine(contentRoot ?? AppContext.BaseDirectory, path);
        }
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        FilePath = path;

        var rc = sqlite3_open_v2(Utf8Z(path), out _db, SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX, IntPtr.Zero);
        if (rc != SQLITE_OK) throw new InvalidOperationException($"Could not open SQLite database at '{path}' (code {rc}).");
        sqlite3_busy_timeout(_db, 5000);

        ExecRaw("PRAGMA journal_mode=WAL;");
        ExecRaw("PRAGMA synchronous=NORMAL;");
        ExecRaw("PRAGMA foreign_keys=ON;");
    }

    /// <summary>INSERT/UPDATE/DELETE/DDL. Returns sqlite3_changes() for DML.</summary>
    public async Task<int> ExecuteAsync(string sql, params DbParam[] args)
    {
        await _gate.WaitAsync();
        try { return RunNonQuery(sql, args); }
        finally { _gate.Release(); }
    }

    public async Task<List<T>> QueryAsync<T>(string sql, Func<DbRow, T> map, params DbParam[] args)
    {
        await _gate.WaitAsync();
        try { return RunQuery(sql, map, args); }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Runs several statements — reads and writes — as one atomic unit, with
    /// the whole app's single writer lock held for its duration (no other
    /// repository call can interleave). Used anywhere a decision depends on
    /// first reading the current state (e.g. "is this RFID card already
    /// linked to someone else?") and then writing — a classic check-then-act
    /// that must not race.
    /// </summary>
    public async Task<T> ExecuteInTransactionAsync<T>(Func<TxContext, T> work)
    {
        await _gate.WaitAsync();
        try
        {
            RunNonQuery("BEGIN IMMEDIATE;", Array.Empty<DbParam>());
            try
            {
                var result = work(new TxContext(this));
                RunNonQuery("COMMIT;", Array.Empty<DbParam>());
                return result;
            }
            catch
            {
                try { RunNonQuery("ROLLBACK;", Array.Empty<DbParam>()); } catch { /* best-effort */ }
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public Task ExecuteInTransactionAsync(Action<TxContext> work) =>
        ExecuteInTransactionAsync<object?>(tx => { work(tx); return null; });

    /// <summary>Handed to the callback of ExecuteInTransactionAsync — the gate is already held, so these call straight into the sync helpers.</summary>
    public sealed class TxContext
    {
        private readonly Database _db;
        internal TxContext(Database db) { _db = db; }
        public int Execute(string sql, params DbParam[] args) => _db.RunNonQuery(sql, args);
        public List<T> Query<T>(string sql, Func<DbRow, T> map, params DbParam[] args) => _db.RunQuery(sql, map, args);
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(string sql, Func<DbRow, T> map, params DbParam[] args) where T : class
    {
        var rows = await QueryAsync(sql, map, args);
        return rows.Count > 0 ? rows[0] : null;
    }

    public async Task<T?> QuerySingleOrDefaultValueAsync<T>(string sql, Func<DbRow, T> map, params DbParam[] args) where T : struct
    {
        var rows = await QueryAsync(sql, map, args);
        return rows.Count > 0 ? rows[0] : null;
    }

    // ---- internals (must run while holding _gate) ----

    private List<T> RunQuery<T>(string sql, Func<DbRow, T> map, DbParam[] args)
    {
        var results = new List<T>();
        var stmt = Prepare(sql, args);
        try
        {
            int rc;
            while ((rc = sqlite3_step(stmt)) == SQLITE_ROW)
            {
                results.Add(map(new DbRow(stmt)));
            }
            if (rc != SQLITE_DONE) ThrowLastError($"step '{sql}'");
        }
        finally { sqlite3_finalize(stmt); }
        return results;
    }

    private int RunNonQuery(string sql, DbParam[] args)
    {
        var stmt = Prepare(sql, args);
        try
        {
            var rc = sqlite3_step(stmt);
            if (rc != SQLITE_DONE && rc != SQLITE_ROW) ThrowLastError($"exec '{sql}'");
            return sqlite3_changes(_db);
        }
        finally { sqlite3_finalize(stmt); }
    }

    private void ExecRaw(string sql) => RunNonQuery(sql, Array.Empty<DbParam>());

    private IntPtr Prepare(string sql, DbParam[] args)
    {
        var rc = sqlite3_prepare_v2(_db, Utf8Z(sql), -1, out var stmt, IntPtr.Zero);
        if (rc != SQLITE_OK) ThrowLastError($"prepare '{sql}'");
        for (var i = 0; i < args.Length; i++)
        {
            BindParam(stmt, i + 1, args[i].Value);
        }
        return stmt;
    }

    private void BindParam(IntPtr stmt, int index, object? value)
    {
        int rc = value switch
        {
            null => sqlite3_bind_null(stmt, index),
            string s => sqlite3_bind_text(stmt, index, Utf8Z(s), -1, new IntPtr(SQLITE_TRANSIENT_NEG1)),
            long l => sqlite3_bind_int64(stmt, index, l),
            int i => sqlite3_bind_int64(stmt, index, i),
            double d => sqlite3_bind_double(stmt, index, d),
            _ => throw new NotSupportedException($"Unsupported parameter type {value.GetType()}.")
        };
        if (rc != SQLITE_OK) ThrowLastError("bind parameter");
    }

    private void ThrowLastError(string context)
    {
        var msg = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(sqlite3_errmsg(_db));
        throw new InvalidOperationException($"SQLite error during {context}: {msg}");
    }

    public void Dispose()
    {
        sqlite3_close(_db);
        _gate.Dispose();
    }
}
