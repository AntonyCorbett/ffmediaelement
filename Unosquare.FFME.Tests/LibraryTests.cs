namespace Unosquare.FFME.Tests;

using Fixtures;
using System;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// Tests for <see cref="Library"/> static methods.
/// Requires FFME_FFMPEG_DIR to be set to the FFmpeg binary directory.
/// </summary>
[Collection(FfmpegCollection.Name)]
public sealed class LibraryTests
{
    private readonly FfmpegFixture _ffmpeg;

    public LibraryTests(FfmpegFixture ffmpeg) => _ffmpeg = ffmpeg;

    // -------------------------------------------------------------------------
    // Initialization state
    // -------------------------------------------------------------------------

    [Fact]
    public void IsInitialized_AfterLoadFFmpeg_IsTrue()
    {
        if (!_ffmpeg.IsAvailable) Assert.Skip(SkipReason);
        Assert.True(Library.IsInitialized);
    }

    [Fact]
    public void FFmpegVersionInfo_AfterLoad_IsNotNullOrEmpty()
    {
        if (!_ffmpeg.IsAvailable) Assert.Skip(SkipReason);
        Assert.False(string.IsNullOrWhiteSpace(Library.FFmpegVersionInfo));
    }

    [Fact]
    public void LoadFFmpeg_CalledAgain_ReturnsTrue()
    {
        // LoadFFmpeg is idempotent — subsequent calls still return true
        if (!_ffmpeg.IsAvailable) Assert.Skip(SkipReason);
        Assert.True(Library.LoadFFmpeg());
    }

    [Fact]
    public void FFmpegDirectory_AfterInit_CannotBeChanged()
    {
        if (!_ffmpeg.IsAvailable) Assert.Skip(SkipReason);
        var original = Library.FFmpegDirectory;
        Library.FFmpegDirectory = "some/other/path";
        // Setting after init is a no-op
        Assert.Equal(original, Library.FFmpegDirectory);
    }

    // -------------------------------------------------------------------------
    // Codec / format enumeration
    // -------------------------------------------------------------------------

    [Fact]
    public void InputFormatNames_IsNotEmpty()
    {
        if (!_ffmpeg.IsAvailable) Assert.Skip(SkipReason);
        Assert.NotEmpty(Library.InputFormatNames);
    }

    [Fact]
    public void InputFormatNames_ContainsCommonFormats()
    {
        if (!_ffmpeg.IsAvailable) Assert.Skip(SkipReason);
        // FFmpeg's MP4 format is registered as "mov,mp4,m4a,3gp,3g2,mj2" not just "mp4"
        Assert.Contains(Library.InputFormatNames, n => n.Contains("mp4"));
        Assert.Contains(Library.InputFormatNames, n => n.Contains("matroska"));
    }

    [Fact]
    public void DecoderNames_IsNotEmpty()
    {
        if (!_ffmpeg.IsAvailable) Assert.Skip(SkipReason);
        Assert.NotEmpty(Library.DecoderNames);
    }

    [Fact]
    public void DecoderNames_ContainsCommonCodecs()
    {
        if (!_ffmpeg.IsAvailable) Assert.Skip(SkipReason);
        Assert.Contains("h264", Library.DecoderNames);
        Assert.Contains("aac", Library.DecoderNames);
    }

    [Fact]
    public void EncoderNames_IsNotEmpty()
    {
        if (!_ffmpeg.IsAvailable) Assert.Skip(SkipReason);
        Assert.NotEmpty(Library.EncoderNames);
    }

    // -------------------------------------------------------------------------
    // RetrieveMediaInfo
    // -------------------------------------------------------------------------

    [Fact]
    public void RetrieveMediaInfo_WithWavFile_ReturnsAudioStream()
    {
        if (!_ffmpeg.IsAvailable) Assert.Skip(SkipReason);

        var path = CreateTempWav();
        try
        {
            var info = Library.RetrieveMediaInfo(path);
            Assert.NotNull(info);
            Assert.True(info.Streams.Count > 0);
            Assert.Contains(info.BestStreams.Keys, k => k == FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_AUDIO);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RetrieveMediaInfo_WithWavFile_PopulatesMetadata()
    {
        if (!_ffmpeg.IsAvailable) Assert.Skip(SkipReason);

        var path = CreateTempWav();
        try
        {
            var info = Library.RetrieveMediaInfo(path);
            Assert.NotNull(info);
            Assert.NotNull(info.Metadata);
            Assert.NotEmpty(info.MediaSource);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // -------------------------------------------------------------------------
    // LogLevel
    // -------------------------------------------------------------------------

    [Fact]
    public void FFmpegLogLevel_CanBeSetAndRead()
    {
        if (!_ffmpeg.IsAvailable) Assert.Skip(SkipReason);

        var original = Library.FFmpegLogLevel;
        Library.FFmpegLogLevel = FFmpeg.AutoGen.ffmpeg.AV_LOG_ERROR;
        Assert.Equal(FFmpeg.AutoGen.ffmpeg.AV_LOG_ERROR, Library.FFmpegLogLevel);
        Library.FFmpegLogLevel = original;
    }

    // -------------------------------------------------------------------------

    private const string SkipReason =
        $"Set {FfmpegFixture.FfmpegDirEnvVar} to the FFmpeg binary directory to run these tests.";

    /// <summary>
    /// Writes a minimal 1-second, 44100 Hz, mono, 16-bit PCM WAV file to a temp path and returns it.
    /// Caller is responsible for deleting the file.
    /// </summary>
    private static string CreateTempWav()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ffme_test_{Guid.NewGuid():N}.wav");
        const int sampleRate = 44100;
        const int dataSize = sampleRate * 2; // 1s × 1ch × 2 bytes/sample

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        bw.Write("RIFF"u8.ToArray());
        bw.Write(36 + dataSize);
        bw.Write("WAVE"u8.ToArray());

        bw.Write("fmt "u8.ToArray());
        bw.Write(16);
        bw.Write((short)1);       // PCM
        bw.Write((short)1);       // mono
        bw.Write(sampleRate);
        bw.Write(sampleRate * 2); // byte rate
        bw.Write((short)2);       // block align
        bw.Write((short)16);      // bits per sample

        bw.Write("data"u8.ToArray());
        bw.Write(dataSize);
        bw.Write(new byte[dataSize]); // silence

        return path;
    }
}
