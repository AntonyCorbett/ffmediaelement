namespace Unosquare.FFME.Tests;

using Playlists;
using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using Xunit;

/// <summary>
/// Tests for <see cref="PlaylistEntryCollection"/> and <see cref="PlaylistEntry"/>.
/// All tests are pure C# — no FFmpeg binaries required.
/// </summary>
public sealed class PlaylistTests
{
    // -------------------------------------------------------------------------
    // Save / Load round-trips
    // -------------------------------------------------------------------------

    [Fact]
    public void SaveLoad_SingleEntry_PreservesAllFields()
    {
        var playlist = new PlaylistEntryCollection { Name = "My Playlist" };
        playlist.Add("Track One", TimeSpan.FromSeconds(90), "http://example.com/1.mp3");

        var loaded = RoundTrip(playlist);

        Assert.Single(loaded);
        Assert.Equal("Track One", loaded[0].Title);
        Assert.Equal("http://example.com/1.mp3", loaded[0].MediaSource);
        // Duration is stored as whole seconds in #EXTINF
        Assert.Equal(90, (int)loaded[0].Duration.TotalSeconds);
    }

    [Fact]
    public void SaveLoad_MultipleEntries_AllPreserved()
    {
        var playlist = new PlaylistEntryCollection { Name = "Test" };
        playlist.Add("Alpha", TimeSpan.FromSeconds(10), "http://a.com/a.mp3");
        playlist.Add("Beta", TimeSpan.FromSeconds(20), "http://b.com/b.mp3");
        playlist.Add("Gamma", TimeSpan.FromSeconds(30), "http://c.com/c.mp3");

        var loaded = RoundTrip(playlist);

        Assert.Equal(3, loaded.Count);
        Assert.Equal("Alpha", loaded[0].Title);
        Assert.Equal("Beta", loaded[1].Title);
        Assert.Equal("Gamma", loaded[2].Title);
    }

    [Fact]
    public void SaveLoad_MediaSourcePreserved()
    {
        var playlist = new PlaylistEntryCollection { Name = "P" };
        playlist.Add("T", TimeSpan.FromSeconds(60), "rtsp://stream.example.com/live");

        var loaded = RoundTrip(playlist);

        Assert.Equal("rtsp://stream.example.com/live", loaded[0].MediaSource);
    }

    [Fact]
    public void SaveLoad_NamePreservedInCollection()
    {
        var playlist = new PlaylistEntryCollection { Name = "My Collection" };
        playlist.Add("T", TimeSpan.FromSeconds(1), "http://x.com/a.mp3");

        var loaded = RoundTrip(playlist);

        Assert.Equal("My Collection", loaded.Name);
    }

    [Fact]
    public void SaveLoad_ZeroDuration_SurvivedRoundTrip()
    {
        var playlist = new PlaylistEntryCollection { Name = "P" };
        playlist.Add("Live Stream", TimeSpan.Zero, "http://example.com/live");

        var loaded = RoundTrip(playlist);

        Assert.Equal(TimeSpan.Zero, loaded[0].Duration);
    }

    [Fact]
    public void SaveLoad_DurationIsStoredAsWholeSeconds()
    {
        var playlist = new PlaylistEntryCollection { Name = "P" };
        // #EXTINF stores seconds via Convert.ToInt64, which rounds to nearest integer.
        // 90.4 → rounds to 90; 90.9 → rounds to 91.
        playlist.Add("T", TimeSpan.FromSeconds(90.4), "http://x.com/a.mp3");

        var loaded = RoundTrip(playlist);

        Assert.Equal(90, (int)loaded[0].Duration.TotalSeconds);
    }

    // -------------------------------------------------------------------------
    // PlaylistEntry property change notification
    // -------------------------------------------------------------------------

    [Fact]
    public void PlaylistEntry_SetTitle_RaisesPropertyChanged()
    {
        var entry = new PlaylistEntry();
        string? changedProperty = null;
        entry.PropertyChanged += (_, e) => changedProperty = e.PropertyName;

        entry.Title = "New Title";

        Assert.Equal(nameof(PlaylistEntry.Title), changedProperty);
    }

    [Fact]
    public void PlaylistEntry_SetSameValue_DoesNotRaisePropertyChanged()
    {
        var entry = new PlaylistEntry { Title = "Same" };
        var raised = false;
        entry.PropertyChanged += (_, _) => raised = true;

        entry.Title = "Same";

        Assert.False(raised);
    }

    [Fact]
    public void PlaylistEntry_SetMediaSource_RaisesPropertyChanged()
    {
        var entry = new PlaylistEntry();
        var events = new List<string>();
        entry.PropertyChanged += (_, e) => events.Add(e.PropertyName!);

        entry.MediaSource = "http://a.com";
        entry.Duration = TimeSpan.FromSeconds(1);

        Assert.Contains(nameof(PlaylistEntry.MediaSource), events);
        Assert.Contains(nameof(PlaylistEntry.Duration), events);
    }

    // -------------------------------------------------------------------------
    // PlaylistEntry attributes
    // -------------------------------------------------------------------------

    [Fact]
    public void PlaylistEntry_Attributes_StoreAndRetrieve()
    {
        var entry = new PlaylistEntry();
        entry.Attributes["custom-key"] = "custom-value";
        Assert.Equal("custom-value", entry.Attributes["custom-key"]);
    }

    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

    private static PlaylistEntryCollection RoundTrip(PlaylistEntryCollection source)
    {
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            source.Save(ms, Encoding.UTF8);
            // StreamWriter inside Save closes the stream on dispose, but MemoryStream.ToArray()
            // is documented to work even on a closed MemoryStream.
            bytes = ms.ToArray();
        }

        using var readMs = new MemoryStream(bytes);
        var result = new PlaylistEntryCollection();
        result.Load(readMs, Encoding.UTF8);
        return result;
    }
}
