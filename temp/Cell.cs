using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ConcurrentPriorityQueue;

namespace Polylabel;

public class Cell
{
    // public float X { get; }
    // public float Y { get; }
    // public float H { get; }
    // public float D { get; }
    // public float Max { get; }

    public float X;
    public float Y;
    public float H;
    public float D;
    public float Max;

    public float NsX1;
    public float NsY1;
    public float NsX2;
    public float NsY2;

    public Cell() { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Cell(float x, float y, float h, float d)
    {
        X = x;
        Y = y;
        H = h;
        D = d;
        Max = d + h * 1.4142135623730951f; // SQRT2
    }
}

internal readonly struct MaxDoubleComparer : IComparer<float>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Compare(float x, float y) => y.CompareTo(x);
}

internal interface ICellQueue
{
    void Enqueue(Cell cell);
    Cell Dequeue();
    int Count { get; }
}

internal readonly struct NativeCellQueue : ICellQueue
{
    private readonly ConcurrentPriorityQueue<Cell, float> _queue;

    public NativeCellQueue()
    {
        _queue = new ConcurrentPriorityQueue<Cell, float>(new MaxDoubleComparer());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Enqueue(Cell cell) => _queue.Enqueue(cell, cell.Max);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Cell Dequeue() => _queue.Dequeue();

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _queue.Count;
    }
}
