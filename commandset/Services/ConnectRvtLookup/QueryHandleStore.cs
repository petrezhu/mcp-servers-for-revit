using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace RevitMCPCommandSet.Services.ConnectRvtLookup;

public static class QueryHandleTypes
{
    public const string Object = "object";
    public const string Value = "value";
}

public sealed class QueryHandleEntry
{
    public string Handle { get; set; }
    public string HandleType { get; set; }
    public string DocumentKey { get; set; }
    public string ContextKey { get; set; }
    public object Value { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    public bool IsExpired(DateTime utcNow)
    {
        return utcNow >= ExpiresAtUtc;
    }
}

public sealed class QueryHandleStore
{
    private readonly ConcurrentDictionary<string, QueryHandleEntry> _entries = new(StringComparer.Ordinal);
    private readonly Func<DateTime> _utcNowProvider;
    private readonly TimeSpan _defaultTimeToLive;
    private long _seed;

    public QueryHandleStore(TimeSpan? defaultTimeToLive = null, Func<DateTime> utcNowProvider = null)
    {
        _defaultTimeToLive = defaultTimeToLive ?? TimeSpan.FromMinutes(20);
        _utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
    }

    public int Count
    {
        get
        {
            PurgeExpired();
            return _entries.Count;
        }
    }

    public string RegisterObjectHandle(string documentKey, object value, string contextKey = null, TimeSpan? timeToLive = null)
    {
        return RegisterHandle("obj", QueryHandleTypes.Object, documentKey, value, contextKey, timeToLive);
    }

    public string RegisterValueHandle(string documentKey, object value, string contextKey = null, TimeSpan? timeToLive = null)
    {
        return RegisterHandle("val", QueryHandleTypes.Value, documentKey, value, contextKey, timeToLive);
    }

    public bool TryResolve(string handle, out QueryHandleEntry entry)
    {
        PurgeExpired();
        if (string.IsNullOrWhiteSpace(handle))
        {
            entry = null;
            return false;
        }

        if (!_entries.TryGetValue(handle, out entry))
        {
            entry = null;
            return false;
        }

        if (!entry.IsExpired(_utcNowProvider()))
        {
            return true;
        }

        _entries.TryRemove(handle, out _);
        entry = null;
        return false;
    }

    public bool TryResolveValue<T>(string handle, out T value)
    {
        if (TryResolve(handle, out var entry) && entry.Value is T typedValue)
        {
            value = typedValue;
            return true;
        }

        value = default;
        return false;
    }

    public bool Remove(string handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            return false;
        }

        return _entries.TryRemove(handle, out _);
    }

    public int InvalidateDocument(string documentKey)
    {
        if (string.IsNullOrWhiteSpace(documentKey))
        {
            return 0;
        }

        return RemoveWhere(entry => string.Equals(entry.DocumentKey, documentKey, StringComparison.Ordinal));
    }

    public int InvalidateContext(string contextKey)
    {
        if (string.IsNullOrWhiteSpace(contextKey))
        {
            return 0;
        }

        return RemoveWhere(entry => string.Equals(entry.ContextKey, contextKey, StringComparison.Ordinal));
    }

    public int InvalidateAll()
    {
        var removed = _entries.Count;
        _entries.Clear();
        return removed;
    }

    public int PurgeExpired()
    {
        var now = _utcNowProvider();
        return RemoveWhere(entry => entry.IsExpired(now));
    }

    private string RegisterHandle(string prefix, string handleType, string documentKey, object value, string contextKey, TimeSpan? timeToLive)
    {
        if (string.IsNullOrWhiteSpace(documentKey))
        {
            throw new ArgumentException("documentKey is required", nameof(documentKey));
        }

        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        PurgeExpired();

        foreach (var existingEntry in _entries.Values)
        {
            if (existingEntry.IsExpired(_utcNowProvider()))
            {
                continue;
            }

            if (string.Equals(existingEntry.HandleType, handleType, StringComparison.Ordinal) &&
                string.Equals(existingEntry.DocumentKey, documentKey, StringComparison.Ordinal) &&
                string.Equals(existingEntry.ContextKey, contextKey, StringComparison.Ordinal) &&
                ReferenceEqualityComparer.Instance.Equals(existingEntry.Value, value))
            {
                return existingEntry.Handle;
            }
        }

        var now = _utcNowProvider();
        var ttl = timeToLive ?? _defaultTimeToLive;
        var handle = $"{prefix}:{Interlocked.Increment(ref _seed):x}";

        _entries[handle] = new QueryHandleEntry
        {
            Handle = handle,
            HandleType = handleType,
            DocumentKey = documentKey,
            ContextKey = contextKey,
            Value = value,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(ttl)
        };

        return handle;
    }

    private int RemoveWhere(Func<QueryHandleEntry, bool> predicate)
    {
        var removed = 0;
        foreach (var pair in _entries.ToArray())
        {
            if (!predicate(pair.Value))
            {
                continue;
            }

            if (_entries.TryRemove(pair.Key, out _))
            {
                removed++;
            }
        }

        return removed;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static ReferenceEqualityComparer Instance { get; } = new();

        public new bool Equals(object x, object y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(object obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}
