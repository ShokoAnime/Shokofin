using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using AsyncKeyedLock;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Shokofin.Utils;

internal class GuardedMemoryCache : IDisposable, IMemoryCache {
    private readonly MemoryCacheOptions CacheOptions;

    private readonly MemoryCacheEntryOptions? CacheEntryOptions;

    private readonly ILogger Logger;

    private IMemoryCache Cache;

    private static readonly AsyncKeyedLockOptions AsyncKeyedLockOptions = new() { MaxCount = 1, PoolSize = 50 };

    private AsyncKeyedLocker<object> Semaphores = new(AsyncKeyedLockOptions);

    public GuardedMemoryCache(ILogger logger, MemoryCacheOptions options, MemoryCacheEntryOptions? cacheEntryOptions = null) {
        Logger = logger;
        CacheOptions = options;
        CacheEntryOptions = cacheEntryOptions;
        Cache = new MemoryCache(CacheOptions);
    }

    public void Clear() {
        Logger.LogDebug("Clearing cache…");
        // TODO: Improve this logic. Currently it should only be ran programmatically after all interactions with the cache has been done, but in cases it's cleared before that it may result in a bad state.
        var cache = Cache;
        var semaphores = Semaphores;

        Cache = new MemoryCache(CacheOptions);
        Semaphores = new(AsyncKeyedLockOptions);

        semaphores.Dispose();
        cache.Dispose();
    }

    public TItem GetOrCreate<TItem>(object key, Action<TItem> foundAction, Func<TItem> createFactory, MemoryCacheEntryOptions? createOptions = null, CancellationToken cancellationToken = default) {
        if (TryGetValue<TItem>(key, out var value)) {
            foundAction(value);
            return value;
        }

        try {
            using (Semaphores.Lock(key, cancellationToken)) {
                cancellationToken.ThrowIfCancellationRequested();

                if (TryGetValue(key, out value)) {
                    foundAction(value);
                    return value;
                }

                using var entry = Cache.CreateEntry(key);
                createOptions ??= CacheEntryOptions;
                if (createOptions != null)
                    entry.SetOptions(createOptions);

                value = createFactory();
                entry.Value = value;
                return value;
            }
        }
        catch (SemaphoreFullException) {
            Logger.LogWarning("Got a semaphore full exception for key: {Key}", key);

            if (value is not null) {
                Logger.LogInformation("Recovered from the semaphore full exception because the value was assigned for key: {Key}", key);
                return value;
            }

            if (TryGetValue(key, out value)) {
                Logger.LogInformation("Recovered from the semaphore full exception because the value was in the cache for key: {Key}", key);
                foundAction(value);
                return value;
            }

            throw;
        }
        catch (Exception ex) {
            Logger.LogTrace(ex, "Got an unexpected exception for key: {Key}", key);
            throw;
        }
    }

    public TItem GetOrCreate<TItem>(object key, Action<TItem> foundAction, Func<GuardedMemoryCacheEntryOptions, TItem> createFactory, CancellationToken cancellationToken = default) {
        if (TryGetValue<TItem>(key, out var value)) {
            foundAction(value);
            return value;
        }

        try {
            using (Semaphores.Lock(key, cancellationToken)) {
                cancellationToken.ThrowIfCancellationRequested();

                if (TryGetValue(key, out value)) {
                    foundAction(value);
                    return value;
                }

                var createOptions = CreateNewOptions();
                value = createFactory(createOptions);
                if (!createOptions.NoCache) {
                    using var entry = Cache.CreateEntry(key);
                    entry.SetOptions(createOptions);
                    entry.Value = value;
                }
                return value;
            }
        }
        catch (SemaphoreFullException) {
            Logger.LogWarning("Got a semaphore full exception for key: {Key}", key);

            if (value is not null) {
                Logger.LogInformation("Recovered from the semaphore full exception because the value was assigned for key: {Key}", key);
                return value;
            }

            if (TryGetValue(key, out value)) {
                Logger.LogInformation("Recovered from the semaphore full exception because the value was in the cache for key: {Key}", key);
                foundAction(value);
                return value;
            }

            throw;
        }
        catch (Exception ex) {
            Logger.LogTrace(ex, "Got an unexpected exception for key: {Key}", key);
            throw;
        }
    }

    public async Task<TItem> GetOrCreateAsync<TItem>(object key, Action<TItem> foundAction, Func<Task<TItem>> createFactory, MemoryCacheEntryOptions? createOptions = null, CancellationToken cancellationToken = default) {
        if (TryGetValue<TItem>(key, out var value)) {
            foundAction(value);
            return value;
        }

        try {
            using (await Semaphores.LockAsync(key, cancellationToken)) {
                cancellationToken.ThrowIfCancellationRequested();

                if (TryGetValue(key, out value)) {
                    foundAction(value);
                    return value;
                }

                using var entry = Cache.CreateEntry(key);
                createOptions ??= CacheEntryOptions;
                if (createOptions != null)
                    entry.SetOptions(createOptions);

                value = await createFactory();
                entry.Value = value;
                return value;
            }
        }
        catch (SemaphoreFullException) {
            Logger.LogWarning("Got a semaphore full exception for key: {Key}", key);

            if (value is not null) {
                Logger.LogInformation("Recovered from the semaphore full exception because the value was assigned for key: {Key}", key);
                return value;
            }

            if (TryGetValue(key, out value)) {
                Logger.LogInformation("Recovered from the semaphore full exception because the value was in the cache for key: {Key}", key);
                foundAction(value);
                return value;
            }

            throw;
        }
        catch (Exception ex) {
            Logger.LogTrace(ex, "Got an unexpected exception for key: {Key}", key);
            throw;
        }
    }

    public async Task<TItem> GetOrCreateAsync<TItem>(object key, Action<TItem> foundAction, Func<GuardedMemoryCacheEntryOptions, Task<TItem>> createFactory, CancellationToken cancellationToken = default) {
        if (TryGetValue<TItem>(key, out var value)) {
            foundAction(value);
            return value;
        }

        try {
            using (await Semaphores.LockAsync(key, cancellationToken)) {
                cancellationToken.ThrowIfCancellationRequested();

                if (TryGetValue(key, out value)) {
                    foundAction(value);
                    return value;
                }

                var createOptions = CreateNewOptions();
                value = await createFactory(createOptions);
                if (!createOptions.NoCache) {
                    using var entry = Cache.CreateEntry(key);
                    entry.SetOptions(createOptions);
                    entry.Value = value;
                }
                return value;
            }
        }
        catch (SemaphoreFullException) {
            Logger.LogWarning("Got a semaphore full exception for key: {Key}", key);

            if (value is not null) {
                Logger.LogInformation("Recovered from the semaphore full exception because the value was assigned for key: {Key}", key);
                return value;
            }

            if (TryGetValue(key, out value)) {
                Logger.LogInformation("Recovered from the semaphore full exception because the value was in the cache for key: {Key}", key);
                foundAction(value);
                return value;
            }

            throw;
        }
        catch (Exception ex) {
            Logger.LogTrace(ex, "Got an unexpected exception for key: {Key}", key);
            throw;
        }
    }

    public TItem GetOrCreate<TItem>(object key, Func<TItem> createFactory, MemoryCacheEntryOptions? createOptions = null, CancellationToken cancellationToken = default) {
        if (TryGetValue<TItem>(key, out var value))
            return value;

        try {
            using (Semaphores.Lock(key, cancellationToken)) {
                cancellationToken.ThrowIfCancellationRequested();

                if (TryGetValue(key, out value))
                    return value;

                using var entry = Cache.CreateEntry(key);
                createOptions ??= CacheEntryOptions;
                if (createOptions != null)
                    entry.SetOptions(createOptions);

                value = createFactory();
                entry.Value = value;
                return value;
            }
        }
        catch (SemaphoreFullException) {
            Logger.LogWarning("Got a semaphore full exception for key: {Key}", key);

            if (value is not null) {
                Logger.LogInformation("Recovered from the semaphore full exception because the value was assigned for key: {Key}", key);
                return value;
            }

            if (TryGetValue(key, out value)) {
                Logger.LogInformation("Recovered from the semaphore full exception because the value was in the cache for key: {Key}", key);
                return value;
            }

            throw;
        }
        catch (Exception ex) {
            Logger.LogTrace(ex, "Got an unexpected exception for key: {Key}", key);
            throw;
        }
    }

    public TItem GetOrCreate<TItem>(object key, Func<GuardedMemoryCacheEntryOptions, TItem> createFactory, CancellationToken cancellationToken = default) {
        if (TryGetValue<TItem>(key, out var value))
            return value;

        try {
            using (Semaphores.Lock(key, cancellationToken)) {
                cancellationToken.ThrowIfCancellationRequested();

                if (TryGetValue(key, out value))
                    return value;

                var createOptions = CreateNewOptions();
                value = createFactory(createOptions);
                if (!createOptions.NoCache) {
                    using var entry = Cache.CreateEntry(key);
                    entry.SetOptions(createOptions);
                    entry.Value = value;
                }
                return value;
            }
        }
        catch (SemaphoreFullException) {
            Logger.LogWarning("Got a semaphore full exception for key: {Key}", key);

            if (value is not null) {
                Logger.LogInformation("Recovered from the semaphore full exception because the value was assigned for key: {Key}", key);
                return value;
            }

            if (TryGetValue(key, out value)) {
                Logger.LogInformation("Recovered from the semaphore full exception because the value was in the cache for key: {Key}", key);
                return value;
            }

            throw;
        }
        catch (Exception ex) {
            Logger.LogTrace(ex, "Got an unexpected exception for key: {Key}", key);
            throw;
        }
    }

    public async Task<TItem> GetOrCreateAsync<TItem>(object key, Func<Task<TItem>> createFactory, MemoryCacheEntryOptions? createOptions = null, CancellationToken cancellationToken = default) {
        if (TryGetValue<TItem>(key, out var value))
            return value;

        try {
            using (await Semaphores.LockAsync(key, cancellationToken)) {
                cancellationToken.ThrowIfCancellationRequested();

                if (TryGetValue(key, out value))
                    return value;

                using var entry = Cache.CreateEntry(key);
                createOptions ??= CacheEntryOptions;
                if (createOptions != null)
                    entry.SetOptions(createOptions);

                value = await createFactory();
                entry.Value = value;
                return value;
            }
        }
        catch (SemaphoreFullException) {
            Logger.LogWarning("Got a semaphore full exception for key: {Key}", key);

            if (value is not null) {
                Logger.LogInformation("Recovered from the semaphore full exception because the value was assigned for key: {Key}", key);
                return value;
            }

            if (TryGetValue(key, out value)) {
                Logger.LogInformation("Recovered from the semaphore full exception because the value was in the cache for key: {Key}", key);
                return value;
            }

            throw;
        }
        catch (Exception ex) {
            Logger.LogTrace(ex, "Got an unexpected exception for key: {Key}", key);
            throw;
        }
    }

    public async Task<TItem> GetOrCreateAsync<TItem>(object key, Func<GuardedMemoryCacheEntryOptions, Task<TItem>> createFactory, CancellationToken cancellationToken = default) {
        if (TryGetValue<TItem>(key, out var value))
            return value;

        try {
            using (await Semaphores.LockAsync(key, cancellationToken)) {
                cancellationToken.ThrowIfCancellationRequested();

                if (TryGetValue(key, out value))
                    return value;

                var createOptions = CreateNewOptions();
                value = await createFactory(createOptions);
                if (!createOptions.NoCache) {
                    using var entry = Cache.CreateEntry(key);
                    entry.SetOptions(createOptions);
                    entry.Value = value;
                }
                return value;
            }
        }
        catch (SemaphoreFullException) {
            Logger.LogWarning("Got a semaphore full exception for key: {Key}", key);

            if (value is not null) {
                Logger.LogInformation("Recovered from the semaphore full exception because the value was assigned for key: {Key}", key);
                return value;
            }

            if (TryGetValue(key, out value)) {
                Logger.LogInformation("Recovered from the semaphore full exception because the value was in the cache for key: {Key}", key);
                return value;
            }

            throw;
        }
        catch (Exception ex) {
            Logger.LogTrace(ex, "Got an unexpected exception for key: {Key}", key);
            throw;
        }
    }

    private GuardedMemoryCacheEntryOptions CreateNewOptions()
        => new() {
            AbsoluteExpiration = CacheEntryOptions?.AbsoluteExpiration is { } aE ? new DateTimeOffset(aE.UtcDateTime.Ticks, aE.Offset) : null,
            AbsoluteExpirationRelativeToNow = CacheEntryOptions?.AbsoluteExpirationRelativeToNow is { } aER ? new TimeSpan(aER.Ticks) : null,
            SlidingExpiration = CacheEntryOptions?.SlidingExpiration is { } sE ? new TimeSpan(sE.Ticks) : null,
            Priority = CacheEntryOptions?.Priority ?? CacheItemPriority.Normal,
            Size = CacheEntryOptions?.Size,
        };

    public void Dispose() {
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
        => Cache.TryGetValue(key, out value);

    public TItem? Set<TItem>(object key, [NotNullIfNotNull(nameof(value))] TItem? value, MemoryCacheEntryOptions? createOptions = null)
        => Cache.Set(key, value, createOptions ?? CacheEntryOptions);

    internal class GuardedMemoryCacheEntryOptions : MemoryCacheEntryOptions {
        /// <summary>
        /// Turns the key into a non-cached lock key to ensure only one thread can process the
        /// value at a time.
        /// </summary>
        public bool NoCache { get; set; } = false;
    }
}
