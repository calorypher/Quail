namespace Quail.App;

internal sealed class BoundedLruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _entries = [];
    private readonly LinkedList<(TKey Key, TValue Value)> _order = [];

    public BoundedLruCache(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public int Count => _entries.Count;

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (_entries.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = default!;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        if (_entries.TryGetValue(key, out var current))
        {
            current.Value = (key, value);
            _order.Remove(current);
            _order.AddFirst(current);
            return;
        }

        var node = _order.AddFirst((key, value));
        _entries.Add(key, node);
        if (_entries.Count <= _capacity) return;
        var leastRecent = _order.Last!;
        _entries.Remove(leastRecent.Value.Key);
        _order.RemoveLast();
    }
}
