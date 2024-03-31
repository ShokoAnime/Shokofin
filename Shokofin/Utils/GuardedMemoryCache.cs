using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace Shokofin.Utils;

sealed class GuardedMemoryCache : IDisposable, IMemoryCache
{
    private readonly IMemoryCache Cache;

    private readonly ConcurrentDictionary<object, SemaphoreSlim> Semaphores = new();

    public GuardedMemoryCache(MemoryCacheOptions options) => Cache = new MemoryCache(options);

    public GuardedMemoryCache(IMemoryCache cache) => Cache = cache;

    public TItem GetOrCreate<TItem>(object key, Action<TItem> foundAction, Func<ICacheEntry, TItem> createFactory, MemoryCacheEntryOptions? createOptions = null)
    {
        if (Cache.TryGetValue<TItem>(key, out var value)) {
            foundAction(value);
            return value;
        }

        var semaphore = GetSemaphore(key);

        semaphore.Wait();

        try
        {
            if (Cache.TryGetValue<TItem>(key, out value)) {
                foundAction(value);
                return value;
            }

            using ICacheEntry entry = Cache.CreateEntry(key);
            if (createOptions != null)
                entry.SetOptions(createOptions);

            value = createFactory(entry);
            entry.Value = value;
            return value;
        }
        finally
        {
            semaphore.Release();
            RemoveSemaphore(key);
        }
    }

    public async Task<TItem> GetOrCreateAsync<TItem>(object key, Action<TItem> foundAction, Func<ICacheEntry, Task<TItem>> createFactory, MemoryCacheEntryOptions? createOptions = null)
    {
        if (Cache.TryGetValue<TItem>(key, out var value)) {
            foundAction(value);
            return value;
        }

        var semaphore = GetSemaphore(key);

        await semaphore.WaitAsync();

        try
        {
            if (Cache.TryGetValue<TItem>(key, out value)) {
                foundAction(value);
                return value;
            }

            using ICacheEntry entry = Cache.CreateEntry(key);
            if (createOptions != null)
                entry.SetOptions(createOptions);

            value = await createFactory(entry).ConfigureAwait(false);
            entry.Value = value;
            return value;
        }
        finally
        {
            semaphore.Release();
            RemoveSemaphore(key);
        }
    }

    public TItem GetOrCreate<TItem>(object key, Func<ICacheEntry, TItem> createFactory, MemoryCacheEntryOptions? createOptions = null)
    {
        if (Cache.TryGetValue<TItem>(key, out var value))
            return value;

        var semaphore = GetSemaphore(key);

        semaphore.Wait();

        try
        {
            if (Cache.TryGetValue<TItem>(key, out value))
                return value;

            using ICacheEntry entry = Cache.CreateEntry(key);
            if (createOptions != null)
                entry.SetOptions(createOptions);

            value = createFactory(entry);
            entry.Value = value;
            return value;
        }
        finally
        {
            semaphore.Release();
            RemoveSemaphore(key);
        }
    }

    public async Task<TItem> GetOrCreateAsync<TItem>(object key, Func<ICacheEntry, Task<TItem>> createFactory, MemoryCacheEntryOptions? createOptions = null)
    {
        if (Cache.TryGetValue<TItem>(key, out var value))
            return value;

        var semaphore = GetSemaphore(key);

        await semaphore.WaitAsync();

        try
        {
            if (Cache.TryGetValue<TItem>(key, out value))
                return value;

            using ICacheEntry entry = Cache.CreateEntry(key);
            if (createOptions != null)
                entry.SetOptions(createOptions);

            value = await createFactory(entry).ConfigureAwait(false);
            entry.Value = value;
            return value;
        }
        finally
        {
            semaphore.Release();
            RemoveSemaphore(key);
        }
    }

    public void Dispose()
    {
        foreach (var semaphore in Semaphores.Values)
            semaphore.Release();
        Cache.Dispose();
    }

    SemaphoreSlim GetSemaphore(object key)
        => Semaphores.GetOrAdd(key, _ => new SemaphoreSlim(1));

    void RemoveSemaphore(object key)
    {
        Semaphores.TryRemove(key, out var _);
    }

    public ICacheEntry CreateEntry(object key)
        => Cache.CreateEntry(key);

    public void Remove(object key)
        => Cache.Remove(key);

    public bool TryGetValue(object key, out object value)
        => Cache.TryGetValue(key, out value);
}