using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Archevolt;

internal sealed class WorkerPool : IDisposable
{
    private static readonly long s_spinBeforeBlockingTicks = TimeSpan.FromMicroseconds(50).Ticks * Stopwatch.Frequency / TimeSpan.TicksPerSecond;
    private readonly object _gate = new();
    private readonly Thread[] _workers;
    private readonly long _spinTicks;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private Exception?[] _errors = [];
    private Action<int>? _execute;
    private CancellationToken _cancellation;
    private PaddedSignal _signal;
    private int _ready;
    private int _pending;
    private int _next;
    private int _count;
    private int _failed;
    private PoolState _state;

    internal WorkerPool(int maxConcurrency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
        _workers = new Thread[maxConcurrency - 1];
        bool oversubscribed = maxConcurrency > Environment.ProcessorCount;
        _spinTicks = oversubscribed ? 0 : s_spinBeforeBlockingTicks;
        var started = 0;
        try
        {
            for (var i = 0; i < _workers.Length; i++)
            {
                var worker = new Thread(Work)
                {
                    IsBackground = true,
                    Name = $"Archevolt worker {i + 1}"
                };
                _workers[i] = worker;
                if (ExecutionContext.IsFlowSuppressed())
                {
                    worker.Start();
                }
                else
                {
                    using (ExecutionContext.SuppressFlow())
                        worker.Start();
                }
                started++;
            }

            lock (_gate)
                while (_ready != _workers.Length)
                    Monitor.Wait(_gate);
        }
        catch
        {
            lock (_gate)
            {
                _signal.Stopping = true;
                Monitor.PulseAll(_gate);
            }
            for (var i = 0; i < started; i++)
                _workers[i].Join();
            throw;
        }
    }

    internal int MaxConcurrency => _workers.Length + 1;

    internal void Run(int count, Action<int> execute, CancellationToken cancellation = default)
        => Run(count, execute, null, cancellation);

    internal void Run(int count, Action<int> execute, Action? ownerWork, CancellationToken cancellation = default)
    {
        AssertOwner();
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentNullException.ThrowIfNull(execute);

        ObjectDisposedException.ThrowIf(_state == PoolState.Disposed, this);
        if (_state != PoolState.Idle)
            throw new InvalidOperationException("The worker pool is already running.");
        cancellation.ThrowIfCancellationRequested();
        if (count == 0 && ownerWork is null)
            return;

        bool runParallel = _workers.Length != 0 && count != 0 && (count > 1 || ownerWork is not null);
        var epoch = runParallel ? checked(_signal.Epoch + 1) : _signal.Epoch;
        if (_errors.Length < count)
            Array.Resize(ref _errors, count);
        Array.Clear(_errors, 0, count);
        _execute = execute;
        _cancellation = cancellation;
        _count = count;
        _next = -1;
        _failed = 0;
        _state = PoolState.Running;
        if (runParallel)
        {
            lock (_gate)
            {
                _pending = _workers.Length;
                _signal.Parked = false;
                _signal.Epoch = epoch;
                Monitor.PulseAll(_gate);
            }
        }
        else
        {
            Park();
        }

        Exception? ownerFailure = null;
        try
        {
            try
            {
                if (!cancellation.IsCancellationRequested && Volatile.Read(ref _failed) == 0)
                    ownerWork?.Invoke();
            }
            catch (Exception exception)
            {
                ownerFailure = exception;
                Interlocked.Exchange(ref _failed, 1);
            }
            ClaimWork();
        }
        finally
        {
            if (runParallel)
            {
                if (Volatile.Read(ref _pending) != 0)
                {
                    for (long deadline = Stopwatch.GetTimestamp() + _spinTicks;
                        Volatile.Read(ref _pending) != 0 && Stopwatch.GetTimestamp() < deadline;)
                        Thread.SpinWait(1);
                }
                lock (_gate)
                    while (_pending != 0)
                        Monitor.Wait(_gate);
            }
            _execute = null;
            _state = PoolState.Idle;
        }

        if (Volatile.Read(ref _failed) != 0)
        {
            var failures = new List<Exception>();
            if (ownerFailure is not null)
                failures.Add(ownerFailure);
            for (var i = 0; i < count; i++)
            {
                if (_errors[i] is { } failure)
                {
                    failures.Add(failure);
                    _errors[i] = null;
                }
            }
            throw new AggregateException("One or more Archevolt work items failed.", failures);
        }
        cancellation.ThrowIfCancellationRequested();
    }

    // The owner is about to run work alone. Spinning workers see the flag and block at once.
    internal void Park()
    {
        if (!_signal.Parked)
            _signal.Parked = true;
    }

    private void Work()
    {
        ulong observedEpoch = 0;
        lock (_gate)
        {
            _ready++;
            Monitor.PulseAll(_gate);
        }

        while (true)
        {
            for (long deadline = Stopwatch.GetTimestamp() + _spinTicks;
                Volatile.Read(ref _signal.Epoch) == observedEpoch && !Volatile.Read(ref _signal.Stopping)
                    && !Volatile.Read(ref _signal.Parked) && Stopwatch.GetTimestamp() < deadline;)
                Thread.SpinWait(1);
            lock (_gate)
            {
                while (!_signal.Stopping && observedEpoch == _signal.Epoch)
                    Monitor.Wait(_gate);
                if (_signal.Stopping)
                    return;
                observedEpoch = _signal.Epoch;
            }

            try
            {
                ClaimWork();
            }
            finally
            {
                lock (_gate)
                {
                    _pending--;
                    if (_pending == 0)
                        Monitor.PulseAll(_gate);
                }
            }
        }
    }

    private void ClaimWork()
    {
        while (Volatile.Read(ref _failed) == 0 && !_cancellation.IsCancellationRequested)
        {
            var index = Interlocked.Increment(ref _next);
            if (index >= _count)
                return;
            try
            {
                _execute!(index);
            }
            catch (Exception exception)
            {
                _errors[index] = exception;
                Interlocked.Exchange(ref _failed, 1);
            }
        }
    }

    public void Dispose()
    {
        AssertOwner();
        lock (_gate)
        {
            if (_state == PoolState.Disposed)
                return;
            if (_state != PoolState.Idle)
                throw new InvalidOperationException("Cannot dispose a running worker pool.");
            _signal.Stopping = true;
            _state = PoolState.Disposed;
            Monitor.PulseAll(_gate);
        }
        foreach (var worker in _workers)
            worker.Join();
        _errors = [];
    }

    private void AssertOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("The worker pool must be controlled by its owner thread.");
    }

    private const int CacheLinePairBytes = 128;

    [StructLayout(LayoutKind.Explicit, Size = 3 * CacheLinePairBytes)]
    private struct PaddedSignal
    {
        [FieldOffset(CacheLinePairBytes)] internal ulong Epoch;
        [FieldOffset(CacheLinePairBytes + sizeof(ulong))] internal bool Stopping;
        [FieldOffset(CacheLinePairBytes + sizeof(ulong) + 1)] internal bool Parked;
    }

    private enum PoolState
    {
        Idle,
        Running,
        Disposed
    }
}
