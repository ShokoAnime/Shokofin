using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using AsyncKeyedLock;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Shokofin.Utils;

sealed class GuardedMemoryCache : IDisposable, IMemoryCache
{
    private readonly MemoryCacheOptions CacheOptions;

    private readonly MemoryCacheEntryOptions? CacheEntryOptions;

    private readonly ILogger Logger;

    private IMemoryCache Cache;

    private static AsyncKeyedLockOptions AsyncKeyedLockOptions = new() { PoolSize = 20, PoolInitialFill = 1 };

    private AsyncKeyedLocker<object> Semaphores = new(AsyncKeyedLockOptions);

    public DateTime LastClearedAt { get; private set; }

    public DateTime LastAccessedAt { get; private set; }

    public readonly TimeSpan StallTime;

    public bool IsStalled => LastAccessedAt - LastClearedAt > StallTime;

    public GuardedMemoryCache(ILogger logger, TimeSpan stallTime, MemoryCacheOptions options, MemoryCacheEntryOptions? cacheEntryOptions = null)
    {
        Logger = logger;
        CacheOptions = options;
        CacheEntryOptions = cacheEntryOptions;
        Cache = new MemoryCache(CacheOptions);
        StallTime = stallTime;
        LastClearedAt = LastAccessedAt = DateTime.Now;
    }

    public void Clear()
    {
        Logger.LogDebug("Clearing cache…");
        var cache = Cache;
        Cache = new MemoryCache(CacheOptions);
        Semaphores.Dispose();
        Semaphores = new AsyncKeyedLocker<object>(AsyncKeyedLockOptions);
        LastClearedAt = LastAccessedAt = DateTime.Now;
        cache.Dispose();
    }

    public TItem GetOrCreate<TItem>(object key, Action<TItem> foundAction, Func<ICacheEntry, TItem> createFactory, MemoryCacheEntryOptions? createOptions = null)
    {
        if (TryGetValue<TItem>(key, out var value)) {
            foundAction(value);
            return value;
        }

        using (Semaphores.Lock(key)) {
            if (TryGetValue(key, out value)) {
                foundAction(value);
                return value;
            }

            using ICacheEntry entry = Cache.CreateEntry(key);
            createOptions ??= CacheEntryOptions;
            if (createOptions != null)
                entry.SetOptions(createOptions);

            value = createFactory(entry);
            entry.Value = value;
            return value;
        }
    }

    public async Task<TItem> GetOrCreateAsync<TItem>(object key, Action<TItem> foundAction, Func<ICacheEntry, Task<TItem>> createFactory, MemoryCacheEntryOptions? createOptions = null)
    {
        if (TryGetValue<TItem>(key, out var value)) {
            foundAction(value);
            return value;
        }

        using (await Semaphores.LockAsync(key).ConfigureAwait(false)) {
            if (TryGetValue(key, out value)) {
                foundAction(value);
                return value;
            }

            using ICacheEntry entry = Cache.CreateEntry(key);
            createOptions ??= CacheEntryOptions;
            if (createOptions != null)
                entry.SetOptions(createOptions);

            value = await createFactory(entry).ConfigureAwait(false);
            entry.Value = value;
            return value;
        }
    }

    public TItem GetOrCreate<TItem>(object key, Func<ICacheEntry, TItem> createFactory, MemoryCacheEntryOptions? createOptions = null)
    {
        if (TryGetValue<TItem>(key, out var value))
            return value;

        using (Semaphores.Lock(key)) {
            if (TryGetValue(key, out value))
                return value;

            using ICacheEntry entry = Cache.CreateEntry(key);
            createOptions ??= CacheEntryOptions;
            if (createOptions != null)
                entry.SetOptions(createOptions);

            value = createFactory(entry);
            entry.Value = value;
            return value;
        }
    }

    public async Task<TItem> GetOrCreateAsync<TItem>(object key, Func<ICacheEntry, Task<TItem>> createFactory, MemoryCacheEntryOptions? createOptions = null)
    {
        if (TryGetValue<TItem>(key, out var value))
            return value;

        using (await Semaphores.LockAsync(key).ConfigureAwait(false)) {
            if (TryGetValue(key, out value))
                return value;

            using ICacheEntry entry = Cache.CreateEntry(key);
            createOptions ??= CacheEntryOptions;
            if (createOptions != null)
                entry.SetOptions(createOptions);

            value = await createFactory(entry).ConfigureAwait(false);
            entry.Value = value;
            return value;
        }
    }

    public void Dispose()
    {
        Semaphores.Dispose();
        Cache.Dispose();
    }

    public ICacheEntry CreateEntry(object key)
        => Cache.CreateEntry(key);

    public void Remove(object key)
        => Cache.Remove(key);

    public bool TryGetValue(object key, [NotNullWhen(true)] out object? value)
        => Cache.TryGetValue(key, out value);

    public bool TryGetValue<TItem>(object key, [NotNullWhen(true)] out TItem? value)
    {
        LastAccessedAt = DateTime.Now;
        return Cache.TryGetValue(key, out value);
    }

    public TItem? Set<TItem>(object key, [NotNullIfNotNull("value")] TItem? value, MemoryCacheEntryOptions? createOptions = null)
    {
        LastAccessedAt = DateTime.Now;
        return Cache.Set(key, value, createOptions ?? CacheEntryOptions);
    }
}