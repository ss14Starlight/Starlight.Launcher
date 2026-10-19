using System.Collections;

namespace Starlight.Launcher.WebUI.Models.Helpers;

/// <summary>
/// Fixed-capacity FIFO buffer. When full, adding an item overwrites the oldest one in O(1).
/// Not thread-safe.
/// </summary>
public sealed class RingBuffer<T> : IReadOnlyList<T>
{
    private readonly T[] _items;
    private int _head;

    public RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _items = new T[capacity];
    }

    public int Capacity => _items.Length;

    public int Count { get; private set; }

    public T this[int index] => (uint)index >= (uint)Count ? throw new ArgumentOutOfRangeException(nameof(index)) : _items[(_head + index) % _items.Length];

    public void Add(T item)
    {
        if (Count < _items.Length)
        {
            _items[(_head + Count) % _items.Length] = item;
            Count++;
            return;
        }

        _items[_head] = item;
        _head = (_head + 1) % _items.Length;
    }

    public void AddRange(IEnumerable<T> items)
    {
        foreach (var item in items)
            Add(item);
    }

    public void Clear()
    {
        Array.Clear(_items);
        _head = 0;
        Count = 0;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
            yield return _items[(_head + i) % _items.Length];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
