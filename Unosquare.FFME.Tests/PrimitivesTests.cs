namespace Unosquare.FFME.Tests;

using Primitives;
using Common;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public sealed class PrimitivesTests
{
    // -------------------------------------------------------------------------
    // WorkerBase lifecycle
    // -------------------------------------------------------------------------

    private sealed class CountingWorker : WorkerBase
    {
        private volatile int _cycles;

        public CountingWorker() : base(nameof(CountingWorker)) { }

        public int Cycles => _cycles;
        public Exception? LastException { get; private set; }

        protected override void ExecuteCycleLogic(CancellationToken ct)
        {
            Interlocked.Increment(ref _cycles);
        }

        protected override void OnCycleException(Exception ex) => LastException = ex;

        // Run very fast for tests
        protected override TimeSpan GetCycleDelay() => TimeSpan.Zero;
    }

    [Fact]
    public void WorkerBase_BeforeStart_StateIsCreated()
    {
        using var w = new CountingWorker();
        Assert.Equal(WorkerState.Created, w.WorkerState);
    }

    [Fact]
    public async Task WorkerBase_AfterStart_StateIsRunning()
    {
        var ct = TestContext.Current.CancellationToken;
        using var w = new CountingWorker();
        await w.StartAsync().WaitAsync(ct);
        Assert.Equal(WorkerState.Running, w.WorkerState);
    }

    [Fact]
    public async Task WorkerBase_Start_CyclesExecute()
    {
        var ct = TestContext.Current.CancellationToken;
        using var w = new CountingWorker();
        await w.StartAsync().WaitAsync(ct);
        await Task.Delay(50, ct);
        Assert.True(w.Cycles > 0, "Expected cycles to have run");
    }

    [Fact]
    public async Task WorkerBase_PauseResume_CyclesStopAndRestart()
    {
        var ct = TestContext.Current.CancellationToken;
        using var w = new CountingWorker();
        await w.StartAsync().WaitAsync(ct);
        await Task.Delay(20, ct);

        await w.PauseAsync().WaitAsync(ct);
        Assert.Equal(WorkerState.Paused, w.WorkerState);

        var cyclesAtPause = w.Cycles;
        await Task.Delay(30, ct);
        Assert.Equal(cyclesAtPause, w.Cycles);

        await w.ResumeAsync().WaitAsync(ct);
        Assert.Equal(WorkerState.Running, w.WorkerState);
        await Task.Delay(20, ct);
        Assert.True(w.Cycles > cyclesAtPause, "Expected cycles to resume after ResumeAsync");
    }

    [Fact]
    public async Task WorkerBase_Stop_StateIsStopped()
    {
        var ct = TestContext.Current.CancellationToken;
        using var w = new CountingWorker();
        await w.StartAsync().WaitAsync(ct);
        await Task.Delay(10, ct);
        await w.StopAsync().WaitAsync(ct);
        Assert.Equal(WorkerState.Stopped, w.WorkerState);
    }

    [Fact]
    public async Task WorkerBase_StopWhilePaused_StateIsStopped()
    {
        var ct = TestContext.Current.CancellationToken;
        using var w = new CountingWorker();
        await w.StartAsync().WaitAsync(ct);
        await w.PauseAsync().WaitAsync(ct);
        await w.StopAsync().WaitAsync(ct);
        Assert.Equal(WorkerState.Stopped, w.WorkerState);
    }

    [Fact]
    public async Task WorkerBase_RequestWakeup_DoesNotThrow()
    {
        var ct = TestContext.Current.CancellationToken;
        using var w = new CountingWorker();
        await w.StartAsync().WaitAsync(ct);
        w.RequestWakeup();
        w.RequestWakeup();
        await w.StopAsync().WaitAsync(ct);
    }

    [Fact]
    public async Task WorkerBase_Dispose_SetsIsDisposed()
    {
        var ct = TestContext.Current.CancellationToken;
        var w = new CountingWorker();
        await w.StartAsync().WaitAsync(ct);
        w.Dispose();
        Assert.True(w.IsDisposed);
    }

    // -------------------------------------------------------------------------
    // CircularBuffer
    // -------------------------------------------------------------------------

    [Fact]
    public void CircularBuffer_New_ReadableIsZeroWritableIsLength()
    {
        using var buf = new CircularBuffer(64);
        Assert.Equal(0, buf.ReadableCount);
        Assert.Equal(64, buf.WritableCount);
        Assert.Equal(64, buf.Length);
    }

    [Fact]
    public void CircularBuffer_Write_ThenRead_ReturnsCorrectData()
    {
        using var buf = new CircularBuffer(64);
        var data = new byte[] { 1, 2, 3, 4, 5 };
        Write(buf, data);

        var result = new byte[5];
        buf.Read(5, result, 0);

        Assert.Equal(data, result);
    }

    [Fact]
    public void CircularBuffer_ReadableAndWritable_SumToLength()
    {
        using var buf = new CircularBuffer(16);
        Write(buf, new byte[10]);
        Assert.Equal(16, buf.ReadableCount + buf.WritableCount);
    }

    [Fact]
    public void CircularBuffer_ReadBeyondAvailable_Throws()
    {
        using var buf = new CircularBuffer(16);
        Write(buf, new byte[4]);
        Assert.Throws<InvalidOperationException>(() => buf.Read(5, new byte[5], 0));
    }

    [Fact]
    public void CircularBuffer_WriteBeyondCapacityNoOverwrite_Throws()
    {
        using var buf = new CircularBuffer(4);
        Write(buf, new byte[4]);
        Assert.Throws<InvalidOperationException>(() => Write(buf, new byte[1], overwrite: false));
    }

    [Fact]
    public void CircularBuffer_Skip_AdvancesReadPosition()
    {
        using var buf = new CircularBuffer(16);
        Write(buf, [10, 20, 30]);
        buf.Skip(2);

        var result = new byte[1];
        buf.Read(1, result, 0);
        Assert.Equal(30, result[0]);
    }

    [Fact]
    public void CircularBuffer_SkipBeyondReadable_Throws()
    {
        using var buf = new CircularBuffer(16);
        Write(buf, new byte[3]);
        Assert.Throws<InvalidOperationException>(() => buf.Skip(4));
    }

    [Fact]
    public void CircularBuffer_Rewind_GoesBackward()
    {
        using var buf = new CircularBuffer(16);
        Write(buf, [1, 2, 3, 4, 5]);

        var first = new byte[2];
        buf.Read(2, first, 0);

        buf.Rewind(2);

        var again = new byte[2];
        buf.Read(2, again, 0);

        Assert.Equal(first, again);
    }

    [Fact]
    public void CircularBuffer_Clear_ResetsAllState()
    {
        using var buf = new CircularBuffer(16);
        Write(buf, new byte[8]);
        buf.Clear();

        Assert.Equal(0, buf.ReadableCount);
        Assert.Equal(16, buf.WritableCount);
        Assert.Equal(TimeSpan.MinValue, buf.WriteTag);
    }

    [Fact]
    public void CircularBuffer_WriteTag_IsPreservedFromLastWrite()
    {
        using var buf = new CircularBuffer(64);
        var tag1 = TimeSpan.FromSeconds(1);
        var tag2 = TimeSpan.FromSeconds(2);

        Write(buf, new byte[4], tag1);
        Write(buf, new byte[4], tag2);

        Assert.Equal(tag2, buf.WriteTag);
    }

    [Fact]
    public void CircularBuffer_WrapAround_ReturnsCorrectData()
    {
        using var buf = new CircularBuffer(8);
        Write(buf, [1, 2, 3, 4, 5, 6]);
        var discard = new byte[6];
        buf.Read(6, discard, 0);

        Write(buf, [7, 8, 9, 10, 11, 12]);

        var result = new byte[6];
        buf.Read(6, result, 0);

        Assert.Equal(new byte[] { 7, 8, 9, 10, 11, 12 }, result);
    }

    [Fact]
    public void CircularBuffer_Dispose_SetsIsDisposed()
    {
        var buf = new CircularBuffer(16);
        buf.Dispose();
        Assert.True(buf.IsDisposed);
    }

    [Fact]
    public void CircularBuffer_CapacityPercent_IsCorrect()
    {
        using var buf = new CircularBuffer(10);
        Write(buf, new byte[5]);
        Assert.Equal(0.5, buf.CapacityPercent);
    }

    // -------------------------------------------------------------------------
    // RealTimeClock
    // -------------------------------------------------------------------------

    [Fact]
    public void RealTimeClock_New_NotRunningPositionZero()
    {
        var clock = new RealTimeClock();
        Assert.False(clock.IsRunning);
        Assert.Equal(TimeSpan.Zero, clock.Position);
    }

    [Fact]
    public void RealTimeClock_Play_SetsIsRunning()
    {
        var clock = new RealTimeClock();
        clock.Play();
        Assert.True(clock.IsRunning);
    }

    [Fact]
    public void RealTimeClock_Pause_StopsIsRunning()
    {
        var clock = new RealTimeClock();
        clock.Play();
        clock.Pause();
        Assert.False(clock.IsRunning);
    }

    [Fact]
    public void RealTimeClock_Reset_StopsAndZerosPosition()
    {
        var clock = new RealTimeClock();
        clock.Update(TimeSpan.FromSeconds(5));
        clock.Play();
        clock.Reset();
        Assert.False(clock.IsRunning);
        Assert.Equal(TimeSpan.Zero, clock.Position);
    }

    [Fact]
    public void RealTimeClock_Update_SetsPositionWhileStopped()
    {
        var clock = new RealTimeClock();
        var target = TimeSpan.FromSeconds(10);
        clock.Update(target);
        Assert.Equal(target, clock.Position);
    }

    [Fact]
    public void RealTimeClock_Update_SetsPositionWhileRunning()
    {
        var clock = new RealTimeClock();
        clock.Play();
        var target = TimeSpan.FromSeconds(10);
        clock.Update(target);
        Assert.True(clock.Position >= target);
        Assert.True(clock.IsRunning);
    }

    [Fact]
    public void RealTimeClock_Restart_StartsFromZero()
    {
        var clock = new RealTimeClock();
        clock.Update(TimeSpan.FromSeconds(5));
        clock.Restart();
        Assert.True(clock.IsRunning);
        Assert.True(clock.Position < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void RealTimeClock_RestartWithOffset_StartsFromOffset()
    {
        var clock = new RealTimeClock();
        var offset = TimeSpan.FromSeconds(5);
        clock.Restart(offset);
        Assert.True(clock.IsRunning);
        Assert.True(clock.Position >= offset);
    }

    [Fact]
    public void RealTimeClock_SpeedRatioZero_PositionFreeze()
    {
        var clock = new RealTimeClock();
        clock.Update(TimeSpan.FromSeconds(3));
        clock.SpeedRatio = 0;
        clock.Play();
        Thread.Sleep(50);
        Assert.Equal(TimeSpan.FromSeconds(3), clock.Position);
    }

    [Fact]
    public void RealTimeClock_NegativeSpeedRatio_ClampedToZero()
    {
        var clock = new RealTimeClock { SpeedRatio = -1 };
        Assert.Equal(0d, clock.SpeedRatio);
    }

    [Fact]
    public void RealTimeClock_PlayCalledTwice_DoesNotThrow()
    {
        var clock = new RealTimeClock();
        clock.Play();
        var ex = Record.Exception(() => clock.Play());
        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // MediaTypeDictionary
    // -------------------------------------------------------------------------

    [Fact]
    public void MediaTypeDictionary_MissingKey_ReturnsDefault()
    {
        var strings = new MediaTypeDictionary<string>();
        Assert.Null(strings[MediaType.Audio]);
        Assert.Null(strings[MediaType.Video]);

        var ints = new MediaTypeDictionary<int>();
        Assert.Equal(0, ints[MediaType.Audio]);
    }

    [Fact]
    public void MediaTypeDictionary_SetKey_GetReturnsValue()
    {
        var dict = new MediaTypeDictionary<string> {[MediaType.Audio] = "test"};
        Assert.Equal("test", dict[MediaType.Audio]);
    }

    [Fact]
    public void MediaTypeDictionary_AllMediaTypes_AreIndependentSlots()
    {
        var dict = new MediaTypeDictionary<int>();
        var types = Enum.GetValues<MediaType>();
        var i = 1;
        foreach (var t in types)
            dict[t] = i++;

        i = 1;
        foreach (var t in types)
            Assert.Equal(i++, dict[t]);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static unsafe void Write(CircularBuffer buffer, byte[] data, TimeSpan tag = default, bool overwrite = false)
    {
        fixed (byte* ptr = data)
            buffer.Write((IntPtr)ptr, data.Length, tag, overwrite);
    }
}
