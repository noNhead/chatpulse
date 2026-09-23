using System.Collections;

namespace ChatPulse.Stats;

/// <summary>
/// Ring-buffer double-ended queue with O(1) indexing.
/// <para>
/// The BCL has no such type: <see cref="Queue{T}"/> drops from the front in O(1) but cannot be
/// indexed, and <see cref="List{T}"/> indexes in O(1) but shifts the whole array on
/// <c>RemoveAt(0)</c>. The stats engine needs both — it evicts expired messages from the front on
/// every snapshot, and binary-searches by timestamp to find the rate window.
/// </para>
/// </summary>
public sealed class Deque<T> : IReadOnlyList<T>
{
    private T[] _items = new T[16];
    private int _head;

    public int Count { get; private set; }

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            return _items[(_head + index) % _items.Length];
        }
    }

    public void AddLast(T item)
    {
        if (Count == _items.Length) Grow();
        _items[(_head + Count) % _items.Length] = item;
        Count++;
    }

    public T RemoveFirst()
    {
        if (Count == 0) throw new InvalidOperationException("Deque is empty.");
        var item = _items[_head];
        _items[_head] = default!; // let the GC reclaim references we no longer own
        _head = (_head + 1) % _items.Length;
        Count--;
        return item;
    }

    public void Clear()
    {
        Array.Clear(_items);
        _head = 0;
        Count = 0;
    }

    private void Grow()
    {
        var grown = new T[_items.Length * 2];
        for (var i = 0; i < Count; i++) grown[i] = _items[(_head + i) % _items.Length];
        _items = grown;
        _head = 0;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < Count; i++) yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
