namespace Archevolt;

internal sealed class WorkerPool : IDisposable
{
    private readonly object _gate = new();
    private readonly Thread[] _workers;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private Exception?[] _errors = [];
    private Action<int>? _execute;
    private CancellationToken _cancellation;
    private ulong _epoch;
    private int _ready;
    private int _pending;
    private int _next;
    private int _count;
    private int _failed;
    private bool _stopping;
    private PoolState _state;

    internal WorkerPool(int maxConcurrency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
        _workers = new Thread[maxConcurrency - 1];
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
                _stopping = true;
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

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_state == PoolState.Disposed, this);
            if (_state != PoolState.Idle)
                throw new InvalidOperationException("The worker pool is already running.");
            cancellation.ThrowIfCancellationRequested();
            if (count == 0 && ownerWork is null)
                return;

            var epoch = checked(_epoch + 1);
            if (_errors.Length < count)
                Array.Resize(ref _errors, count);
            Array.Clear(_errors, 0, count);
            _execute = execute;
            _cancellation = cancellation;
            _count = count;
            _next = -1;
            _failed = 0;
            _pending = _workers.Length;
            _state = PoolState.Running;
            _epoch = epoch;
            Monitor.PulseAll(_gate);
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
            lock (_gate)
            {
                while (_pending != 0)
                    Monitor.Wait(_gate);
                _execute = null;
                _state = PoolState.Idle;
            }
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
            lock (_gate)
            {
                while (!_stopping && observedEpoch == _epoch)
                    Monitor.Wait(_gate);
                if (_stopping)
                    return;
                observedEpoch = _epoch;
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
            _stopping = true;
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

    private enum PoolState
    {
        Idle,
        Running,
        Disposed
    }
}
