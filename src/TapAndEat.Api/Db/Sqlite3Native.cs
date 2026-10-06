using System.Runtime.InteropServices;

namespace TapAndEat.Api.Db;

/// <summary>
/// Raw P/Invoke bindings for libsqlite3. The sandbox this project is built in
/// has no NuGet access (nuget.org is blocked), so Microsoft.Data.Sqlite /
/// EF Core can't be installed — this binds straight to the system
/// libsqlite3.so that ships with the OS instead. On a machine with NuGet
/// access, swap this whole Db/ folder for Microsoft.Data.Sqlite (or EF Core)
/// without touching any repository's public surface — see README "Database".
/// </summary>
internal static class Sqlite3Native
{
    private const string Lib = "libsqlite3.so.0";
    static Sqlite3Native()
    {
        NativeLibrary.SetDllImportResolver(typeof(Sqlite3Native).Assembly, (libraryName, assembly, searchPath) =>
        {
            if (libraryName == Lib)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Windows 10/11 includes winsqlite3.dll out of the box
                    if (NativeLibrary.TryLoad("winsqlite3.dll", assembly, searchPath, out var handle))
                        return handle;

                    // Fallback if sqlite3.dll is placed in the output directory
                    if (NativeLibrary.TryLoad("sqlite3.dll", assembly, searchPath, out handle))
                        return handle;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    if (NativeLibrary.TryLoad("libsqlite3.so.0", assembly, searchPath, out var handle))
                        return handle;
                    if (NativeLibrary.TryLoad("libsqlite3.so", assembly, searchPath, out handle))
                        return handle;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    if (NativeLibrary.TryLoad("libsqlite3.dylib", assembly, searchPath, out var handle))
                        return handle;
                }
            }
            return IntPtr.Zero;
        });
    }
    public const int SQLITE_OK = 0;
    public const int SQLITE_ROW = 100;
    public const int SQLITE_DONE = 101;
    public const int SQLITE_OPEN_READWRITE = 0x00000002;
    public const int SQLITE_OPEN_CREATE = 0x00000004;
    public const int SQLITE_OPEN_FULLMUTEX = 0x00010000; // thread-safe handle (we still serialise writes ourselves)
    public const int SQLITE_TRANSIENT_NEG1 = -1;

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr zVfs);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_close(IntPtr db);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int nByte, out IntPtr stmt, IntPtr tail);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_step(IntPtr stmt);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_finalize(IntPtr stmt);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_reset(IntPtr stmt);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_text")]
    public static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[]? value, int n, IntPtr destructor);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_bind_double(IntPtr stmt, int index, double value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_bind_null(IntPtr stmt, int index);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_column_count(IntPtr stmt);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_column_type(IntPtr stmt, int col);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_column_bytes(IntPtr stmt, int col);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern long sqlite3_column_int64(IntPtr stmt, int col);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern double sqlite3_column_double(IntPtr stmt, int col);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr sqlite3_column_name(IntPtr stmt, int col);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr sqlite3_errmsg(IntPtr db);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern long sqlite3_last_insert_rowid(IntPtr db);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_changes(IntPtr db);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_busy_timeout(IntPtr db, int ms);

    public const int SQLITE_INTEGER = 1;
    public const int SQLITE_FLOAT = 2;
    public const int SQLITE_TEXT = 3;
    public const int SQLITE_BLOB = 4;
    public const int SQLITE_NULL = 5;

    public static string? Utf8PtrToString(IntPtr ptr, int len)
    {
        if (ptr == IntPtr.Zero) return null;
        if (len == 0) return string.Empty;
        var bytes = new byte[len];
        Marshal.Copy(ptr, bytes, 0, len);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    public static byte[] Utf8Z(string s) => System.Text.Encoding.UTF8.GetBytes(s + "\0");
}
