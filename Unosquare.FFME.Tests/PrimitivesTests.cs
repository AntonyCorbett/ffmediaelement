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
    // AtomicBoolean
    // -------------------------------------------------------------------------

    [Fact]
    public void AtomicBoolean_DefaultCtor_IsFalse() =>
        Assert.False(new AtomicBoolean().Value);

    [Fact]
    public void AtomicBoolean_InitTrue_IsTrue() =>
        Assert.True(new AtomicBoolean(true).Value);

    [Fact]
    public void AtomicBoolean_SetValue_Roundtrips()
    {
        var a = new AtomicBoolean(false);
        a.Value = true;
        Assert.True(a.Value);
        a.Value = false;
        Assert.False(a.Value);
    }

    [Fact]
    public void AtomicBoolean_EqualityOperators()
    {
        var a = new AtomicBoolean(true);
        var b = new AtomicBoolean(true);
        Assert.True(a == b);
        Assert.True(a == true);
        Assert.False(a != true);
        b.Value = false;
        Assert.True(a != b);
    }

    [Fact]
    public void AtomicBoolean_Increment_FlipsFalseToTrue()
    {
        var a = new AtomicBoolean(false);
        a.Increment();
        Assert.True(a.Value);
    }

    [Fact]
    public void AtomicBoolean_Decrement_FlipsTrueToFalse()
    {
        var a = new AtomicBoolean(true);
        a.Decrement();
        Assert.False(a.Value);
    }

    [Fact]
    public void AtomicBoolean_NonZeroBackingValue_IsTrue()
    {
        // Any non-zero backing value (not just 1) maps to true
        var a = new AtomicBoolean(true);
        a.Increment(); // backing value goes to 2
        Assert.True(a.Value);
    }

    [Fact]
    public void AtomicBoolean_ConcurrentWrites_AreThreadSafe()
    {
        var a = new AtomicBoolean(false);
        Parallel.For(0, 1000, _ => a.Value = true);
        Assert.True(a.Value);
    }

    // -------------------------------------------------------------------------
    // AtomicInteger
    // -------------------------------------------------------------------------

    [Fact]
    public void AtomicInteger_DefaultCtor_IsZero() =>
        Assert.Equal(0, new AtomicInteger().Value);

    [Fact]
    public void AtomicInteger_InitValue_Roundtrips() =>
        Assert.Equal(42, new AtomicInteger(42).Value);

    [Fact]
    public void AtomicInteger_Increment_AddsByOne()
    {
        var a = new AtomicInteger(5);
        a.Increment();
        Assert.Equal(6, a.Value);
    }

    [Fact]
    public void AtomicInteger_Decrement_SubtractsByOne()
    {
        var a = new AtomicInteger(5);
        a.Decrement();
        Assert.Equal(4, a.Value);
    }

    [Fact]
    public void AtomicInteger_Operators()
    {
        var a = new AtomicInteger(10);
        Assert.True(a > 9);
        Assert.True(a < 11);
        Assert.True(a >= 10);
        Assert.True(a <= 10);
        Assert.True(a == 10);
        Assert.True(a != 9);
    }

    [Fact]
    public void AtomicInteger_ConcurrentIncrements_AreThreadSafe()
    {
        var a = new AtomicInteger(0);
        Parallel.For(0, 1000, _ => a.Increment());
        Assert.Equal(1000, a.Value);
    }

    // -------------------------------------------------------------------------
    // AtomicLong
    // -------------------------------------------------------------------------

    [Fact]
    public void AtomicLong_DefaultCtor_IsZero() =>
        Assert.Equal(0L, new AtomicLong().Value);

    [Fact]
    public void AtomicLong_InitValue_Roundtrips() =>
        Assert.Equal(long.MaxValue, new AtomicLong(long.MaxValue).Value);

    [Fact]
    public void AtomicLong_IncrementDecrement()
    {
        var a = new AtomicLong(0);
        a.Increment();
        Assert.Equal(1L, a.Value);
        a.Decrement();
        Assert.Equal(0L, a.Value);
    }

    // -------------------------------------------------------------------------
    // AtomicDouble
    // -------------------------------------------------------------------------

    [Fact]
    public void AtomicDouble_DefaultCtor_IsZero() =>
        Assert.Equal(0.0, new AtomicDouble().Value);

    [Fact]
    public void AtomicDouble_InitValue_Roundtrips() =>
        Assert.Equal(Math.PI, new AtomicDouble(Math.PI).Value);

    [Fact]
    public void AtomicDouble_PositiveInfinity_Roundtrips()
    {
        var a = new AtomicDouble(double.PositiveInfinity);
        Assert.Equal(double.PositiveInfinity, a.Value);
    }

    [Fact]
    public void AtomicDouble_NegativeInfinity_Roundtrips()
    {
        var a = new AtomicDouble(double.NegativeInfinity);
        Assert.Equal(double.NegativeInfinity, a.Value);
    }

    [Fact]
    public void AtomicDouble_NaN_RoundtripsViaBitConversion()
    {
        // AtomicDouble stores bit representation via BitConverter so NaN round-trips correctly.
        var a = new AtomicDouble(double.NaN);
        Assert.True(double.IsNaN(a.Value));
    }

    // -------------------------------------------------------------------------
    // AtomicTimeSpan
    // -------------------------------------------------------------------------

    [Fact]
    public void AtomicTimeSpan_InitZero_IsZero() =>
        Assert.Equal(TimeSpan.Zero, new AtomicTimeSpan(TimeSpan.Zero).Value);

    [Fact]
    public void AtomicTimeSpan_InitValue_Roundtrips()
    {
        var ts = TimeSpan.FromSeconds(5.5);
        Assert.Equal(ts, new AtomicTimeSpan(ts).Value);
    }

    [Fact]
    public void AtomicTimeSpan_SetValue_UpdatesCorrectly()
    {
        var a = new AtomicTimeSpan(TimeSpan.Zero);
        a.Value = TimeSpan.FromMinutes(2);
        Assert.Equal(TimeSpan.FromMinutes(2), a.Value);
    }

    // -------------------------------------------------------------------------
    // AtomicDateTime
    // -------------------------------------------------------------------------

    [Fact]
    public void AtomicDateTime_SetValue_Roundtrips()
    {
        var now = DateTime.UtcNow;
        var a = new AtomicDateTime(now);
        Assert.Equal(now, a.Value);
    }

    [Fact]
    public void AtomicDateTime_DefaultCtor_IsDefault() =>
        Assert.Equal(default(DateTime), new AtomicDateTime(default).Value);

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
        Write(buf, new byte[] { 10, 20, 30 });
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
        Write(buf, new byte[] { 1, 2, 3, 4, 5 });

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
        // Write 6 bytes to an 8-byte buffer, read 6, then write 6 more (wraps around).
        using var buf = new CircularBuffer(8);
        Write(buf, new byte[] { 1, 2, 3, 4, 5, 6 });
        var discard = new byte[6];
        buf.Read(6, discard, 0); // read index is now at 6

        Write(buf, new byte[] { 7, 8, 9, 10, 11, 12 }); // wraps: 7,8 at pos 6,7; 9,10,11,12 at 0,1,2,3

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
        // Position must be at or beyond the target since clock is still running
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
        // Position should be very close to zero immediately after restart
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
        // With speed ratio 0, position should remain at the update value
        Assert.Equal(TimeSpan.FromSeconds(3), clock.Position);
    }

    [Fact]
    public void RealTimeClock_NegativeSpeedRatio_ClampedToZero()
    {
        var clock = new RealTimeClock();
        clock.SpeedRatio = -1;
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
        var dict = new MediaTypeDictionary<string>();
        dict[MediaType.Audio] = "test";
        Assert.Equal("test", dict[MediaType.Audio]);
    }

    [Fact]
    public void MediaTypeDictionary_AllMediaTypes_AreIndependentSlots()
    {
        var dict = new MediaTypeDictionary<int>();
        var types = Enum.GetValues<MediaType>().Cast<MediaType>().ToArray();
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
