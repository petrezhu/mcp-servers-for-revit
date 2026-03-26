using RevitMCPCommandSet.Services.ConnectRvtLookup;

namespace RevitMCPCommandSet.Tests.ConnectRvtLookup;

public class QueryHandleStoreTests
{
    [Test]
    public async Task RegisterObjectHandle_ThenResolve_ReturnsStoredEntry()
    {
        var store = new QueryHandleStore();
        var payload = new object();

        var handle = store.RegisterObjectHandle("doc-a", payload, "ctx-selection");
        var found = store.TryResolve(handle, out var entry);

        await Assert.That(found).IsTrue();
        await Assert.That(entry).IsNotNull();
        await Assert.That(entry.HandleType).IsEqualTo(QueryHandleTypes.Object);
        await Assert.That(entry.DocumentKey).IsEqualTo("doc-a");
        await Assert.That(entry.ContextKey).IsEqualTo("ctx-selection");
        await Assert.That(ReferenceEquals(entry.Value, payload)).IsTrue();
    }

    [Test]
    public async Task RegisterValueHandle_ThenResolveTypedValue_ReturnsStoredValue()
    {
        var store = new QueryHandleStore();
        var handle = store.RegisterValueHandle("doc-a", "bbox-summary", "ctx-value");

        var found = store.TryResolveValue<string>(handle, out var value);

        await Assert.That(found).IsTrue();
        await Assert.That(value).IsEqualTo("bbox-summary");
    }

    [Test]
    public async Task RegisterSameObjectTwice_ReusesExistingHandle()
    {
        var store = new QueryHandleStore();
        var payload = new object();

        var first = store.RegisterObjectHandle("doc-a", payload, "ctx-selection");
        var second = store.RegisterObjectHandle("doc-a", payload, "ctx-selection");

        await Assert.That(second).IsEqualTo(first);
        await Assert.That(store.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ResolveUnknownHandle_ReturnsFalse()
    {
        var store = new QueryHandleStore();
        var found = store.TryResolve("obj:missing", out var entry);

        await Assert.That(found).IsFalse();
        await Assert.That(entry).IsNull();
    }

    [Test]
    public async Task ExpiredHandle_IsPurgedAndCannotBeResolved()
    {
        var now = new DateTime(2026, 3, 26, 0, 0, 0, DateTimeKind.Utc);
        var store = new QueryHandleStore(
            defaultTimeToLive: TimeSpan.FromMinutes(5),
            utcNowProvider: () => now);

        var handle = store.RegisterObjectHandle("doc-a", new object());

        now = now.AddMinutes(6);

        var found = store.TryResolve(handle, out var entry);

        await Assert.That(found).IsFalse();
        await Assert.That(entry).IsNull();
        await Assert.That(store.Count).IsEqualTo(0);
    }

    [Test]
    public async Task InvalidateDocument_RemovesOnlyMatchingDocumentEntries()
    {
        var store = new QueryHandleStore();
        var handleA = store.RegisterObjectHandle("doc-a", new object());
        var handleB = store.RegisterValueHandle("doc-b", new object());

        var removed = store.InvalidateDocument("doc-a");

        await Assert.That(removed).IsEqualTo(1);
        await Assert.That(store.TryResolve(handleA, out _)).IsFalse();
        await Assert.That(store.TryResolve(handleB, out _)).IsTrue();
    }

    [Test]
    public async Task InvalidateContext_RemovesOnlyMatchingContextEntries()
    {
        var store = new QueryHandleStore();
        var handleA = store.RegisterObjectHandle("doc-a", new object(), "ctx-selection");
        var handleB = store.RegisterValueHandle("doc-a", new object(), "ctx-navigation");

        var removed = store.InvalidateContext("ctx-selection");

        await Assert.That(removed).IsEqualTo(1);
        await Assert.That(store.TryResolve(handleA, out _)).IsFalse();
        await Assert.That(store.TryResolve(handleB, out _)).IsTrue();
    }

    [Test]
    public async Task InvalidateAll_RemovesEveryEntry()
    {
        var store = new QueryHandleStore();
        store.RegisterObjectHandle("doc-a", new object());
        store.RegisterValueHandle("doc-b", new object());

        var removed = store.InvalidateAll();

        await Assert.That(removed).IsEqualTo(2);
        await Assert.That(store.Count).IsEqualTo(0);
    }
}
