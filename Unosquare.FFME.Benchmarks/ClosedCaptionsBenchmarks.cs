namespace Unosquare.FFME.Benchmarks;

using BenchmarkDotNet.Attributes;
using ClosedCaptions;
using System;

/// <summary>
/// Measures EIA-608 closed-caption packet parsing.
/// Packets are created from raw 3-byte payloads in VideoComponent for every
/// video frame that contains CC side-data. Parsing happens on the decode thread;
/// regressions here delay rendering of subtitles.
/// No FFmpeg binaries are required.
/// </summary>
[MemoryDiagnoser]
public class ClosedCaptionsBenchmarks
{
    private static readonly TimeSpan Timestamp = TimeSpan.FromSeconds(1);

    // Representative byte pairs covering the main parsing branches:
    // text, control commands, and null pads.
    private static readonly (byte header, byte d0, byte d1)[] Samples =
    [
        (0xFC, 0x48, 0x69),  // field-1 text "Hi"
        (0xFD, 0x48, 0x69),  // field-2 text "Hi"
        (0xFC, 0x14, 0x20),  // field-1 control: EraseDisplayedMemory
        (0xFC, 0x14, 0x25),  // field-1 control: RollUpCaptions2Rows
        (0xFC, 0x00, 0x00),  // null pad
        (0xF8, 0x48, 0x69),  // invalid header → NullPad
        (0xFC, 0x20, 0x41),  // printable ASCII
        (0xFD, 0x14, 0x2D),  // field-2 control: EraseNonDisplayedMemory
    ];

    [Benchmark(Description = "Parse single packet (text)")]
    public CaptionsPacketType ParseText() =>
        new ClosedCaptionPacket(Timestamp, 0xFC, 0x48, 0x69).PacketType;

    [Benchmark(Description = "Parse single packet (control)")]
    public CaptionsPacketType ParseControl() =>
        new ClosedCaptionPacket(Timestamp, 0xFC, 0x14, 0x20).PacketType;

    [Benchmark(Description = "Parse single packet (null pad)")]
    public CaptionsPacketType ParseNullPad() =>
        new ClosedCaptionPacket(Timestamp, 0xFC, 0x00, 0x00).PacketType;

    [Benchmark(Description = "Parse all sample variants (8 packets)")]
    public int ParseAllVariants()
    {
        var count = 0;
        foreach (var (h, d0, d1) in Samples)
        {
            _ = new ClosedCaptionPacket(Timestamp, h, d0, d1).PacketType;
            count++;
        }

        return count;
    }
}
