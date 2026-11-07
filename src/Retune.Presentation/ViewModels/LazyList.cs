using System.Collections;

namespace Retune.Presentation.ViewModels;

public static class LazyList
{
    public static LazyList<T> Empty<T>()
        where T : class => new(0, _ => throw new InvalidOperationException("The list is empty."));
}

/// <summary>
/// A read-only list that creates each element the first time it's read and then keeps it. Bound to a
/// virtualising panel, only the rows on screen are ever built, so a 65k-item list costs almost nothing.
/// Implements the non-generic <see cref="IList"/> because that's what WPF virtualisation needs.
/// </summary>
public sealed class LazyList<T> : IReadOnlyList<T>, IList
    where T : class
{
    private readonly Func<int, T> _create;
    private readonly T?[] _items;

    public LazyList(int count, Func<int, T> create)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _create = create ?? throw new ArgumentNullException(nameof(create));
        _items = new T?[count];
    }

    public int Count => _items.Length;

    public bool IsReadOnly => true;

    public bool IsFixedSize => true;

    public bool IsSynchronized => false;

    public object SyncRoot => this;

    public T this[int index] => _items[index] ??= _create(index);

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    /// <summary>The elements built so far (to refresh their state without rebuilding the list).</summary>
    public IEnumerable<T> Realized => _items.OfType<T>();

    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < _items.Length; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Contains(object? value) => IndexOf(value) >= 0;

    /// <summary>Only searches realised elements: WPF asks for indexes of items it has displayed.</summary>
    public int IndexOf(object? value)
    {
        for (var i = 0; i < _items.Length; i++)
        {
            if (ReferenceEquals(_items[i], value))
            {
                return i;
            }
        }

        return -1;
    }

    public void CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        for (var i = 0; i < _items.Length; i++)
        {
            array.SetValue(this[i], index + i);
        }
    }

    int IList.Add(object? value) => throw new NotSupportedException();

    void IList.Clear() => throw new NotSupportedException();

    void IList.Insert(int index, object? value) => throw new NotSupportedException();

    void IList.Remove(object? value) => throw new NotSupportedException();

    void IList.RemoveAt(int index) => throw new NotSupportedException();
}
