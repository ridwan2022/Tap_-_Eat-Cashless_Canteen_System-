namespace TapAndEat.Api.Infrastructure;

/// <summary>
/// Async mutual exclusion per string key. Used to serialise the few places
/// where two concurrent requests could otherwise both act on the same order,
/// wallet or user (double payment, double credit, double tap). Entries are
/// reference-counted and removed when idle, so the dictionary doesn't grow
/// without bound. Locks are not re-entrant — never acquire the same key twice
/// in one call chain.
/// </summary>
public static class KeyedLock
{
    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Refs;
    }

    private static readonly Dictionary<string, Entry> Entries = new();

    public static async Task<IDisposable> AcquireAsync(string key)
    {
        Entry entry;
        lock (Entries)
        {
            if (!Entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                Entries[key] = entry;
            }
            entry.Refs++;
        }

        await entry.Semaphore.WaitAsync();
        return new Releaser(key, entry);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly string _key;
        private readonly Entry _entry;
        private int _disposed;

        public Releaser(string key, Entry entry)
        {
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            _entry.Semaphore.Release();
            lock (Entries)
            {
                if (--_entry.Refs == 0)
                {
                    Entries.Remove(_key);
                }
            }
        }
    }
}
