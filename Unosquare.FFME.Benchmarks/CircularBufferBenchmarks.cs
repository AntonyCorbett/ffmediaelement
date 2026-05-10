namespace Unosquare.FFME.Benchmarks;

using BenchmarkDotNet.Attributes;
using Primitives;
using System;
using System.Runtime.InteropServices;

/// <summary>
/// Measures CircularBuffer read/write throughput - the audio renderer's hot path.
/// Every decoded audio frame is written here by FrameDecodingWorker and read by
/// AudioRenderer at ~10ms intervals, so even small regressions in lock overhead
/// or copy speed are audible as glitches.
/// </summary>
[MemoryDiagnoser]
public class CircularBufferBenchmarks : IDisposable
{
    private const int BufferSize = 64 * 1024; // 64 KB, typical audio buffer
    private const int ChunkSize = 4 * 1024;   // 4 KB write/read per operation

    private CircularBuffer _buffer = null!;
    private byte[] _writeData = null!;
    private byte[] _readData = null!;
    private GCHandle _writeHandle;
    private IntPtr _writePtr;

    [GlobalSetup]
    public void Setup()
    {
        _buffer = new CircularBuffer(BufferSize);
        _writeData = new byte[ChunkSize];
        _readData = new byte[ChunkSize];
        new Random(42).NextBytes(_writeData);
        _writeHandle = GCHandle.Alloc(_writeData, GCHandleType.Pinned);
        _writePtr = _writeHandle.AddrOfPinnedObject();
    }

    [GlobalCleanup]
    public void Dispose()
    {
        _writeHandle.Free();
        _buffer.Dispose();
    }

    [Benchmark(Description = "Write 4KB chunk")]
    public void Write()
    {
        if (_buffer.WritableCount < ChunkSize)
            _buffer.Clear();
        _buffer.Write(_writePtr, ChunkSize, TimeSpan.Zero, overwrite: false);
    }

    [Benchmark(Description = "Read 4KB chunk")]
    public void Read()
    {
        if (_buffer.ReadableCount < ChunkSize)
        {
            _buffer.Clear();
            _buffer.Write(_writePtr, ChunkSize, TimeSpan.Zero, overwrite: false);
        }

        _buffer.Read(ChunkSize, _readData, 0);
    }

    [Benchmark(Description = "Write then Read 4KB (round-trip)")]
    public void WriteRead()
    {
        _buffer.Clear();
        _buffer.Write(_writePtr, ChunkSize, TimeSpan.Zero, overwrite: false);
        _buffer.Read(ChunkSize, _readData, 0);
    }
}
