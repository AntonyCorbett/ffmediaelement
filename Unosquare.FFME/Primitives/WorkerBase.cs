namespace Unosquare.FFME.Primitives;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Base class for background workers. Each worker runs its own dedicated
/// background Thread - no thread-pool dependency for the core loop.
/// Pause/resume use a SemaphoreSlim gate; stop uses CancellationTokenSource.
/// A ManualResetEventSlim lets other workers signal an early wakeup.
/// </summary>
internal abstract class WorkerBase : IWorker
{
    /// <summary>
    /// Run gate: 0 = blocked (Created or Paused), 1 = allowed to run.
    /// Exposed as protected so BlockRenderingWorker's high-priority thread can use it directly.
    /// </summary>
    protected readonly SemaphoreSlim RunGate = new(0, 1);

    private readonly CancellationTokenSource _stopCts = new();
    private volatile CancellationTokenSource _cycleCts = new();
    private readonly ManualResetEventSlim _wakeSignal = new(false);
    private readonly ManualResetEventSlim _cycleCompleted = new(true);

    private readonly TaskCompletionSource _loopDone = new();
    private volatile int _stateValue = (int)WorkerState.Created;
    private volatile bool _isDisposed;

    protected WorkerBase(string name) => Name = name;

    public string Name { get; }
    public WorkerState WorkerState => (WorkerState)_stateValue;
    public bool IsDisposed => _isDisposed;
    protected bool IsDisposing { get; private set; }

    /// <summary>Fires when StopAsync is called. Exposed for subclasses with custom threads.</summary>
    protected CancellationToken StopToken => _stopCts.Token;

    /// <summary>
    /// Fires when Interrupt() is called (PauseAsync or StopAsync).
    /// Cycle logic should check this to exit inner loops early.
    /// </summary>
    protected CancellationToken CycleToken => _cycleCts.Token;

    public Task<WorkerState> StartAsync()
    {
        if (_isDisposed) return Task.FromResult(WorkerState);

        if (Interlocked.CompareExchange(ref _stateValue, (int)WorkerState.Running, (int)WorkerState.Created)
            == (int)WorkerState.Created)
        {
            StartWorkerThread();
            RunGate.Release(); // unblock the loop
        }

        return Task.FromResult(WorkerState);
    }

    public async Task<WorkerState> PauseAsync()
    {
        if (_isDisposed) return WorkerState;

        if (Interlocked.CompareExchange(ref _stateValue, (int)WorkerState.Paused, (int)WorkerState.Running)
            != (int)WorkerState.Running)
            return WorkerState;

        // Cancel the current cycle so its inner loops exit promptly,
        // then consume the run gate to prevent another cycle from starting.
        Interrupt();
        await RunGate.WaitAsync(_stopCts.Token).ConfigureAwait(false);
        _cycleCompleted.Wait(_stopCts.Token);
        return WorkerState;
    }

    public Task<WorkerState> ResumeAsync()
    {
        if (_isDisposed) return Task.FromResult(WorkerState);

        if (Interlocked.CompareExchange(ref _stateValue, (int)WorkerState.Running, (int)WorkerState.Paused)
            != (int)WorkerState.Paused)
            return Task.FromResult(WorkerState);

        RunGate.Release();
        return Task.FromResult(WorkerState);
    }

    public async Task<WorkerState> StopAsync()
    {
        if (_isDisposed) return WorkerState;

        var prevState = (WorkerState)Interlocked.Exchange(ref _stateValue, (int)WorkerState.Stopped);
        if (prevState == WorkerState.Stopped || prevState == WorkerState.Created)
            return WorkerState;

        Interrupt();
        _wakeSignal.Set(); // unblock any cycle delay
        await _stopCts.CancelAsync().ConfigureAwait(false);

        try { await _loopDone.Task.ConfigureAwait(false); }
        catch { }

        return WorkerState;
    }

    public virtual void Dispose()
    {
        if (_isDisposed) return;
        IsDisposing = true;

        StopAsync().GetAwaiter().GetResult();
        try { OnDisposing(); } catch { /* ignore errors during cleanup */ }

        RunGate.Dispose();
        _stopCts.Dispose();
        _cycleCts.Dispose();
        _wakeSignal.Dispose();
        _cycleCompleted.Dispose();

        _isDisposed = true;
        IsDisposing = false;
    }

    /// <summary>
    /// Signals this worker to start its next cycle immediately rather than waiting
    /// for the next timer tick. Thread-safe; silently merges duplicate signals.
    /// </summary>
    public void RequestWakeup() => _wakeSignal.Set();

    /// <summary>
    /// Cancels the current cycle's CancellationToken and replaces it with a fresh one.
    /// Called on pause and stop to break out of in-flight cycle logic promptly.
    /// </summary>
    protected void Interrupt()
    {
        var old = Interlocked.Exchange(ref _cycleCts, new CancellationTokenSource());
        try { old.Cancel(); }
        finally { old.Dispose(); }
    }

    /// <summary>
    /// Marks the start of an active worker cycle.
    /// </summary>
    protected void BeginCycle() => _cycleCompleted.Reset();

    /// <summary>
    /// Marks the end of an active worker cycle.
    /// </summary>
    protected void EndCycle() => _cycleCompleted.Set();

    /// <summary>Executes one unit of work. Check ct frequently for responsive interruption.</summary>
    protected abstract void ExecuteCycleLogic(CancellationToken ct);

    /// <summary>Called when the cycle logic throws an unexpected exception.</summary>
    protected virtual void OnCycleException(Exception ex) { }

    /// <summary>Called once, just before the worker is fully disposed.</summary>
    protected virtual void OnDisposing() { }

    /// <summary>
    /// Returns how long to wait between cycles. Default is the standard timing period (~15 ms).
    /// Workers that want to run as fast as possible return TimeSpan.Zero.
    /// </summary>
    protected virtual TimeSpan GetCycleDelay() => Constants.DefaultTimingPeriod;

    /// <summary>
    /// Starts the worker thread. Override to use a different thread (e.g. Highest priority).
    /// The thread body must call <see cref="SignalLoopComplete"/> in its finally block.
    /// </summary>
    protected virtual void StartWorkerThread()
    {
        var thread = new Thread(RunLoop)
        {
            IsBackground = true,
            Name = Name
        };
        thread.Start();
    }

    /// <summary>
    /// Must be called at the end of any thread started by <see cref="StartWorkerThread"/> overrides.
    /// Tells StopAsync that the loop has finished.
    /// </summary>
    protected void SignalLoopComplete() => _loopDone.TrySetResult();

    private void RunLoop()
    {
        try
        {
            while (true)
            {
                // Block when paused or before the first StartAsync; exit on stop.
                try { RunGate.Wait(StopToken); }
                catch (OperationCanceledException) { break; }

                if (StopToken.IsCancellationRequested)
                {
                    RunGate.Release(); // keep the count balanced
                    break;
                }

                RunGate.Release(); // re-release immediately for next iteration

                // Give each cycle a fresh, uncancelled token.
                var oldCts = Interlocked.Exchange(ref _cycleCts, new CancellationTokenSource());
                oldCts.Dispose();

                var ct = _cycleCts.Token;
                BeginCycle();
                try
                {
                    ExecuteCycleLogic(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // cycle interrupted via Interrupt() — continue the loop
                }
                catch (Exception ex)
                {
                    OnCycleException(ex);
                }
                finally
                {
                    EndCycle();
                }

                WaitForNextCycle();
            }
        }
        finally
        {
            _loopDone.TrySetResult();
        }
    }

    private void WaitForNextCycle()
    {
        var delay = GetCycleDelay();
        if (delay <= TimeSpan.Zero) return;

        try
        {
            // Wait for the delay, but wake up early if another worker signals us or stop is requested.
            _wakeSignal.Wait(delay, StopToken);
        }
        catch (OperationCanceledException) { }

        _wakeSignal.Reset(); // reset for the next wait
    }
}
