using System.Runtime.CompilerServices;

namespace PolyLabelNet
{
    /// <summary>
    /// An <see cref="ICellQueue"/> backed by <see cref="Tinyqueue{T}"/>, ordering cells so the one
    /// with the largest <see cref="Cell.Max"/> is dequeued first.
    /// </summary>
    /// <remarks>
    /// This is a class (not a struct) because C# 7.3 forbids parameterless struct constructors, and
    /// the queue must be initialized on construction.
    /// </remarks>
    internal sealed class TinyCellQueue : ICellQueue
    {
        private readonly Tinyqueue<Cell> _queue;

        public TinyCellQueue()
        {
            _queue = new Tinyqueue<Cell>(compare: (a, b) => b.Max.CompareTo(a.Max));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue(Cell cell) => _queue.Push(cell);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Cell Dequeue() => _queue.Pop();

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _queue.Length;
        }
    }
}