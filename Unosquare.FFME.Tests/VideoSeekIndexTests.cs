namespace Unosquare.FFME.Tests;

using Common;
using System;
using System.IO;
using Xunit;

/// <summary>
/// Tests for <see cref="VideoSeekIndex"/> and <see cref="VideoSeekIndexEntry"/>.
/// All tests are pure C# — no FFmpeg binaries required.
/// </summary>
public sealed class VideoSeekIndexTests
{
    // -------------------------------------------------------------------------
    // VideoSeekIndex construction
    // -------------------------------------------------------------------------

    [Fact]
    public void Construction_SetsProperties()
    {
        var index = new VideoSeekIndex("my-source.mp4", 3);
        Assert.Equal("my-source.mp4", index.MediaSource);
        Assert.Equal(3, index.StreamIndex);
        Assert.Empty(index.Entries);
    }

    // -------------------------------------------------------------------------
    // VideoSeekIndexEntry construction & CSV round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public void Entry_Properties_AreSetCorrectly()
    {
        var entry = MakeEntry(0, startTicks: 10_000_000, pts: 1000, dts: 900);
        Assert.Equal(0, entry.StreamIndex);
        Assert.Equal(TimeSpan.FromTicks(10_000_000), entry.StartTime);
        Assert.Equal(1000L, entry.PresentationTime);
        Assert.Equal(900L, entry.DecodingTime);
        Assert.Equal(1, entry.StreamTimeBase.num);
        Assert.Equal(90000, entry.StreamTimeBase.den);
    }

    [Fact]
    public void Entry_ToCsvString_FromCsvString_RoundTrips()
    {
        var entry = MakeEntry(0, 50_000_000, 5000, 4900);
        var csv = entry.ToCsvString();
        var loaded = VideoSeekIndexEntry.FromCsvString(csv);

        Assert.NotNull(loaded);
        Assert.Equal(entry.StreamIndex, loaded.StreamIndex);
        Assert.Equal(entry.StartTime, loaded.StartTime);
        Assert.Equal(entry.PresentationTime, loaded.PresentationTime);
        Assert.Equal(entry.DecodingTime, loaded.DecodingTime);
        Assert.Equal(entry.StreamTimeBase.num, loaded.StreamTimeBase.num);
        Assert.Equal(entry.StreamTimeBase.den, loaded.StreamTimeBase.den);
    }

    [Fact]
    public void Entry_FromCsvString_WithMalformedLine_ReturnsNull()
    {
        Assert.Null(VideoSeekIndexEntry.FromCsvString("not,enough,fields"));
        Assert.Null(VideoSeekIndexEntry.FromCsvString(""));
        Assert.Null(VideoSeekIndexEntry.FromCsvString("bad,1,90000,100,200,300"));
    }

    // -------------------------------------------------------------------------
    // VideoSeekIndexEntry comparison operators
    // -------------------------------------------------------------------------

    [Fact]
    public void Entry_CompareTo_SortsByStartTime()
    {
        var e1 = MakeEntry(0, 100);
        var e2 = MakeEntry(0, 200);
        Assert.True(e1.CompareTo(e2) < 0);
        Assert.True(e2.CompareTo(e1) > 0);
        Assert.Equal(0, e1.CompareTo(e1));
    }

    [Fact]
    public void Entry_CompareToTimeSpan_UsesStartTime()
    {
        var e = MakeEntry(0, startTicks: TimeSpan.FromSeconds(5).Ticks);
        Assert.Equal(0, e.CompareTo(TimeSpan.FromSeconds(5)));
        Assert.True(e.CompareTo(TimeSpan.FromSeconds(6)) < 0);
        Assert.True(e.CompareTo(TimeSpan.FromSeconds(4)) > 0);
    }

    [Fact]
    public void Entry_Operators_WorkCorrectly()
    {
        var e1 = MakeEntry(0, 100);
        var e2 = MakeEntry(0, 200);
        Assert.True(e1 < e2);
        Assert.True(e2 > e1);
        Assert.True(e1 <= e2);
        Assert.True(e2 >= e1);
        Assert.True(e1 != e2);
    }

    [Fact]
    public void Entry_Equals_UsesReferenceEquality()
    {
        var e1 = MakeEntry(0, 100);
        var e2 = MakeEntry(0, 100); // same values, different instance
        Assert.False(e1.Equals(e2));
        Assert.True(e1.Equals(e1));
    }

    // -------------------------------------------------------------------------
    // Find
    // -------------------------------------------------------------------------

    [Fact]
    public void Find_EmptyEntries_ReturnsNull()
    {
        var index = new VideoSeekIndex("src", 0);
        Assert.Null(index.Find(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Find_SeekAtOrBeforeFirstEntry_ReturnsNull()
    {
        // StartIndexOf edge case: returns -1 when seekTarget <= first entry's StartTime.
        var index = IndexWithSortedEntries(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4));

        Assert.Null(index.Find(TimeSpan.FromSeconds(1)));  // strictly before first
        Assert.Null(index.Find(TimeSpan.FromSeconds(2)));  // exactly at first → still null
    }

    [Fact]
    public void Find_ExactMatchOnNonFirstEntry_ReturnsEntry()
    {
        // Exact match works for any entry that is NOT the first in the list.
        // The algorithm has a fast-exit when seekTarget <= first entry, so a 0s
        // leading entry is needed to avoid that path.
        var t = TimeSpan.FromSeconds(2);
        var index = IndexWithSortedEntries(TimeSpan.FromSeconds(0), t, TimeSpan.FromSeconds(4));

        var result = index.Find(t);
        Assert.NotNull(result);
        Assert.Equal(t, result.StartTime);
    }

    [Fact]
    public void Find_BetweenEntries_ReturnsEarlierEntry()
    {
        var index = IndexWithSortedEntries(
            TimeSpan.FromSeconds(0),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10));

        var result = index.Find(TimeSpan.FromSeconds(3));
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromSeconds(0), result.StartTime);
    }

    [Fact]
    public void Find_AfterAllEntries_ReturnsLastEntry()
    {
        var last = TimeSpan.FromSeconds(10);
        var index = IndexWithSortedEntries(TimeSpan.FromSeconds(5), last);

        var result = index.Find(TimeSpan.FromSeconds(999));
        Assert.NotNull(result);
        Assert.Equal(last, result.StartTime);
    }

    // -------------------------------------------------------------------------
    // Save / Load round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public void SaveLoad_RoundTrip_PreservesAllData()
    {
        var original = new VideoSeekIndex("file://path/to/video.mp4", 2);
        original.Entries.Add(MakeEntry(2, 0, 0, 0));
        original.Entries.Add(MakeEntry(2, TimeSpan.FromSeconds(1).Ticks, 90000, 90000));
        original.Entries.Add(MakeEntry(2, TimeSpan.FromSeconds(2).Ticks, 180000, 180000));

        using var ms = new MemoryStream();
        original.Save(ms);

        ms.Position = 0;
        var loaded = VideoSeekIndex.Load(ms);

        Assert.Equal(original.MediaSource, loaded.MediaSource);
        Assert.Equal(original.StreamIndex, loaded.StreamIndex);
        Assert.Equal(original.Entries.Count, loaded.Entries.Count);

        for (var i = 0; i < original.Entries.Count; i++)
        {
            Assert.Equal(original.Entries[i].StartTime, loaded.Entries[i].StartTime);
            Assert.Equal(original.Entries[i].PresentationTime, loaded.Entries[i].PresentationTime);
            Assert.Equal(original.Entries[i].DecodingTime, loaded.Entries[i].DecodingTime);
        }
    }

    [Fact]
    public void SaveLoad_MediaSourceWithQuotes_IsEscapedAndRestored()
    {
        const string source = "file://path/with/\"quotes\"/video.mp4";
        var original = new VideoSeekIndex(source, 0);

        using var ms = new MemoryStream();
        original.Save(ms);
        ms.Position = 0;
        var loaded = VideoSeekIndex.Load(ms);

        Assert.Equal(source, loaded.MediaSource);
    }

    // -------------------------------------------------------------------------
    // ComputeMonotonicDistance
    // -------------------------------------------------------------------------

    [Fact]
    public void ComputeMonotonicDistance_EmptyIndex_ReturnsNegativeOne()
    {
        var index = new VideoSeekIndex("s", 0);
        Assert.Equal(-1L, index.ComputeMonotonicDistance());
    }

    [Fact]
    public void ComputeMonotonicDistance_SingleEntry_ReturnsNegativeOne()
    {
        var index = new VideoSeekIndex("s", 0);
        index.Entries.Add(MakeEntry(0, 0, 0, 0));
        Assert.Equal(-1L, index.ComputeMonotonicDistance());
    }

    [Fact]
    public void ComputeMonotonicDistance_MonotonicEntries_ReturnsStep()
    {
        var index = new VideoSeekIndex("s", 0);
        // PTS at 0, 3000, 6000, 9000 → step = 3000
        index.Entries.Add(MakeEntry(0, 0, pts: 0, dts: 0));
        index.Entries.Add(MakeEntry(0, 1, pts: 3000, dts: 3000));
        index.Entries.Add(MakeEntry(0, 2, pts: 6000, dts: 6000));
        index.Entries.Add(MakeEntry(0, 3, pts: 9000, dts: 9000));

        Assert.Equal(3000L, index.ComputeMonotonicDistance());
    }

    [Fact]
    public void ComputeMonotonicDistance_NonMonotonicEntries_ReturnsNegativeOne()
    {
        var index = new VideoSeekIndex("s", 0);
        index.Entries.Add(MakeEntry(0, 0, pts: 0, dts: 0));
        index.Entries.Add(MakeEntry(0, 1, pts: 3000, dts: 3000));
        index.Entries.Add(MakeEntry(0, 2, pts: 7000, dts: 7000)); // different step

        Assert.Equal(-1L, index.ComputeMonotonicDistance());
    }

    // -------------------------------------------------------------------------
    // AddMonotonicEntries
    // -------------------------------------------------------------------------

    [Fact]
    public void AddMonotonicEntries_LessThanTwo_DoesNothing()
    {
        var index = new VideoSeekIndex("s", 0);
        index.Entries.Add(MakeEntry(0, 0, 0, 0));
        index.AddMonotonicEntries(TimeSpan.FromSeconds(10));
        Assert.Single(index.Entries);
    }

    [Fact]
    public void AddMonotonicEntries_ExtendsToStreamDuration()
    {
        var index = new VideoSeekIndex("s", 0);
        // Two entries 1 second apart
        var oneSec = TimeSpan.FromSeconds(1).Ticks;
        index.Entries.Add(MakeEntry(0, startTicks: 0, pts: 0, dts: 0));
        index.Entries.Add(MakeEntry(0, startTicks: oneSec, pts: 90000, dts: 90000));

        index.AddMonotonicEntries(TimeSpan.FromSeconds(3));

        Assert.True(index.Entries.Count > 2);
        Assert.True(index.Entries[^1].StartTime <= TimeSpan.FromSeconds(3));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static VideoSeekIndexEntry MakeEntry(
        int streamIndex, long startTicks = 0, long pts = 0, long dts = 0) =>
        new(streamIndex, 1, 90000, startTicks, pts, dts);

    private static VideoSeekIndex IndexWithSortedEntries(params TimeSpan[] times)
    {
        var index = new VideoSeekIndex("src", 0);
        foreach (var t in times)
            index.Entries.Add(MakeEntry(0, t.Ticks));
        index.Entries.Sort((a, b) => a.StartTime.Ticks.CompareTo(b.StartTime.Ticks));
        return index;
    }
}
