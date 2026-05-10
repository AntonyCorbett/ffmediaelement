namespace Unosquare.FFME.Benchmarks;

using BenchmarkDotNet.Attributes;
using Primitives;
using System;

/// <summary>
/// Measures RealTimeClock position reads and updates.
/// All three worker threads and the 15 ms UI timer read Position concurrently;
/// SpeedRatio is written by the command manager. Lock contention here shows up
/// as timing jitter and A/V sync drift.
/// </summary>
[MemoryDiagnoser]
public class RealTimeClockBenchmarks
{
    private RealTimeClock _runningClock = null!;
    private RealTimeClock _stoppedClock = null!;

    [GlobalSetup]
    public void Setup()
    {
        _runningClock = new RealTimeClock();
        _runningClock.Play();

        _stoppedClock = new RealTimeClock();
        _stoppedClock.Update(TimeSpan.FromSeconds(42));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _runningClock.Pause();
        _stoppedClock.Pause();
    }

    [Benchmark(Description = "Position read (running clock)")]
    public TimeSpan PositionRunning() => _runningClock.Position;

    [Benchmark(Description = "Position read (stopped clock)")]
    public TimeSpan PositionStopped() => _stoppedClock.Position;

    [Benchmark(Description = "Update position")]
    public void Update() => _runningClock.Update(TimeSpan.FromSeconds(1));

    [Benchmark(Description = "SpeedRatio set + Position read (simulates seek)")]
    public TimeSpan SpeedRatioChange()
    {
        _runningClock.SpeedRatio = 1.0;
        return _runningClock.Position;
    }
}
