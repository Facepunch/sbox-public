using System;
using System.Threading;
using BepuUtilities;
using BepuUtilities.Memory;

namespace Sandbox.Engine.Emulation.Physics;

/// <summary>
/// A simple implementation of IThreadDispatcher for BepuPhysics.
/// </summary>
public class SimpleThreadDispatcher : IThreadDispatcher, IDisposable
{
    private readonly int _threadCount;
    private readonly Task[] _workers;
    private readonly AutoResetEvent[] _workerSignals;
    private readonly AutoResetEvent _completionSignal;
    private Action<int>? _workerAction;
    private unsafe delegate*<int, IThreadDispatcher, void> _workerPtr;
    private volatile bool _disposed;
    private volatile int _completedWorkers;

    public int ThreadCount => _threadCount;
    public unsafe void* UnmanagedContext { get; set; }
    public object? ManagedContext { get; set; }
    public WorkerBufferPools WorkerPools { get; private set; }

    public SimpleThreadDispatcher(int threadCount)
    {
        _threadCount = threadCount;
        _workers = new Task[threadCount];
        _workerSignals = new AutoResetEvent[threadCount];
        _completionSignal = new AutoResetEvent(false);
        
        WorkerPools = new WorkerBufferPools(threadCount); 

        for (int i = 0; i < threadCount; ++i)
        {
            _workerSignals[i] = new AutoResetEvent(false);
            int workerIndex = i;
            _workers[i] = Task.Factory.StartNew(() => WorkerLoop(workerIndex), TaskCreationOptions.LongRunning);
        }
    }

    private unsafe void WorkerLoop(int workerIndex)
    {
        while (!_disposed)
        {
            _workerSignals[workerIndex].WaitOne();
            if (_disposed) break;

            if (_workerAction != null)
            {
                _workerAction(workerIndex);
            }
            else if (_workerPtr != null)
            {
                _workerPtr(workerIndex, this);
            }

            if (Interlocked.Increment(ref _completedWorkers) == _threadCount)
            {
                _completionSignal.Set();
            }
        }
    }

    public unsafe void DispatchWorkers(Action<int> workerBody, int maximumThreadCount, void* unmanagedContext, object? managedContext)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SimpleThreadDispatcher));

        _workerAction = workerBody;
        _workerPtr = null;
        _completedWorkers = 0;
        
        UnmanagedContext = unmanagedContext;
        ManagedContext = managedContext;
        
        for (int i = 0; i < _threadCount; ++i)
        {
            _workerSignals[i].Set();
        }

        _completionSignal.WaitOne();
    }

    public unsafe void DispatchWorkers(delegate*<int, IThreadDispatcher, void> workerBody, int maximumThreadCount, void* unmanagedContext, object? managedContext)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SimpleThreadDispatcher));

        _workerPtr = workerBody;
        _workerAction = null;
        _completedWorkers = 0;
        
        UnmanagedContext = unmanagedContext;
        ManagedContext = managedContext;
        
        for (int i = 0; i < _threadCount; ++i)
        {
            _workerSignals[i].Set();
        }

        _completionSignal.WaitOne();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            for (int i = 0; i < _threadCount; ++i)
            {
                _workerSignals[i].Set();
                // We could wait for tasks to finish here
            }
            
            foreach (var signal in _workerSignals)
            {
                signal.Dispose();
            }
            _completionSignal.Dispose();
        }
    }
}
