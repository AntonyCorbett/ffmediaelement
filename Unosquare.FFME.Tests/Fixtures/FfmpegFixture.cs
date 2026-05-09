namespace Unosquare.FFME.Tests.Fixtures;

using FFmpeg.AutoGen;
using System;
using Xunit;

/// <summary>
/// Initialises the FFmpeg library once per test session.
/// Set the FFME_FFMPEG_DIR environment variable to the folder containing the
/// FFmpeg shared DLLs (e.g. avcodec-61.dll). Tests are skipped when the
/// variable is absent or FFmpeg fails to load.
/// </summary>
public sealed class FfmpegFixture : IDisposable
{
    public const string FfmpegDirEnvVar = "FFME_FFMPEG_DIR";

    public FfmpegFixture()
    {
        var dir = Environment.GetEnvironmentVariable(FfmpegDirEnvVar);
        if (string.IsNullOrWhiteSpace(dir))
        {
            IsAvailable = false;
            return;
        }

        Library.FFmpegDirectory = dir;
        IsAvailable = Library.LoadFFmpeg();

        if (IsAvailable)
        {
            // lavfi (test video/audio sources) lives in libavdevice and needs explicit registration.
            // Without this, av_find_input_format("lavfi") returns null and FFmpeg falls back to
            // file-path detection, producing ENOENT for filter-graph strings like "testsrc=...".
            ffmpeg.avdevice_register_all();
        }
    }

    /// <summary>
    /// True when FFmpeg was located and loaded successfully.
    /// </summary>
    public bool IsAvailable { get; }

    public void Dispose() { } // FFmpeg cannot be unloaded at runtime
}

[CollectionDefinition(Name)]
public sealed class FfmpegCollection : ICollectionFixture<FfmpegFixture>
{
    public const string Name = "FFmpeg";
}
