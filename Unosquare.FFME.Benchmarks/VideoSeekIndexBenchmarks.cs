namespace Unosquare.FFME.Benchmarks;

using BenchmarkDotNet.Attributes;
using Common;
using System;
using System.IO;

/// <summary>
/// Measures VideoSeekIndex find and I/O operations.
/// Seek operations call Find() to locate the nearest keyframe entry before
/// dispatching a seek command. A regression here adds latency to every
/// user-initiated seek and to the internal coalesced-seek queue drain.
/// No FFmpeg binaries are required.
/// </summary>
[MemoryDiagnoser]
public class VideoSeekIndexBenchmarks
{
    // Simulate a 2-hour film at one keyframe per second → 7200 entries.
    private const int EntryCount = 7_200;
    private const long PtsStep = 90_000; // 90 kHz timebase, 1-second keyframes

    private VideoSeekIndex _index = null!;
    private TimeSpan _seekNearStart;
    private TimeSpan _seekMidpoint;
    private TimeSpan _seekNearEnd;
    private byte[] _serialized = null!;

    [GlobalSetup]
    public void Setup()
    {
        _index = new VideoSeekIndex("benchmark://fake-source.mp4", streamIndex: 0);

        for (var i = 0; i < EntryCount; i++)
        {
            var startTicks = TimeSpan.FromSeconds(i).Ticks;
            var pts = (long)i * PtsStep;
            _index.Entries.Add(new VideoSeekIndexEntry(0, 1, 90_000, startTicks, pts, pts));
        }

        _seekNearStart = TimeSpan.FromSeconds(1);
        _seekMidpoint = TimeSpan.FromSeconds(EntryCount / 2);
        _seekNearEnd = TimeSpan.FromSeconds(EntryCount - 2);

        using var ms = new MemoryStream();
        _index.Save(ms);
        _serialized = ms.ToArray();
    }

    [Benchmark(Description = "Find near start (early exit)")]
    public bool FindNearStart() => _index.Find(_seekNearStart) is not null;

    [Benchmark(Description = "Find at midpoint (binary search)")]
    public bool FindMidpoint() => _index.Find(_seekMidpoint) is not null;

    [Benchmark(Description = "Find near end (worst-case scan)")]
    public bool FindNearEnd() => _index.Find(_seekNearEnd) is not null;

    [Benchmark(Description = "Save index to MemoryStream")]
    public long Save()
    {
        using var ms = new MemoryStream();
        _index.Save(ms);
        return ms.Length;
    }

    [Benchmark(Description = "Load index from MemoryStream")]
    public int Load()
    {
        using var ms = new MemoryStream(_serialized);
        var loaded = VideoSeekIndex.Load(ms);
        return loaded.Entries.Count;
    }
}
