namespace Unosquare.FFME.Tests;

using Common;
using Container;
using Fixtures;
using System;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// Integration tests for <see cref="MediaContainer"/> exercising the full
/// Initialize → Open → Read → Decode → Convert pipeline.
///
/// These tests use FFmpeg's built-in lavfi virtual input device so no media
/// files are required. They are skipped when the FFME_FFMPEG_DIR environment
/// variable is not set or FFmpeg fails to load.
///
/// Requirements:
///   - Set FFME_FFMPEG_DIR to the folder containing the FFmpeg shared DLLs.
///   - The FFmpeg build must include lavfi (standard in pre-built shared releases).
/// </summary>
[Collection(FfmpegCollection.Name)]
public sealed class MediaContainerTests
{
    private readonly FfmpegFixture _ffmpeg;

    public MediaContainerTests(FfmpegFixture ffmpeg) => _ffmpeg = ffmpeg;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    [Fact]
    public void Constructor_WithNullSource_ThrowsArgumentNullException() =>
        Assert.Throws<ArgumentNullException>(() => new MediaContainer((string)null!, null, null));

    [Fact]
    public void Constructor_WithWhitespaceSource_ThrowsArgumentNullException() =>
        Assert.Throws<ArgumentNullException>(() => new MediaContainer("   ", null, null));

    // -------------------------------------------------------------------------
    // Initialize
    // -------------------------------------------------------------------------

    [SkippableFact]
    public void Initialize_WithVideoSource_SetsIsInitializedAndPopulatesMediaInfo()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        using var container = CreateVideoContainer();
        container.Initialize();

        Assert.True(container.IsInitialized);
        Assert.NotNull(container.MediaInfo);
        Assert.NotEmpty(container.MediaFormatName);
        Assert.NotNull(container.Metadata);
    }

    [SkippableFact]
    public void Initialize_CalledTwice_ThrowsInvalidOperationException()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        using var container = CreateVideoContainer();
        container.Initialize();

        Assert.Throws<InvalidOperationException>(container.Initialize);
    }

    // -------------------------------------------------------------------------
    // Open
    // -------------------------------------------------------------------------

    [SkippableFact]
    public void Open_AfterDispose_ThrowsObjectDisposedException()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        var container = CreateVideoContainer();
        container.Initialize();
        container.Dispose();

        Assert.Throws<ObjectDisposedException>(container.Open);
    }

    [SkippableFact]
    public void Open_BeforeInitialize_ThrowsInvalidOperationException()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        using var container = CreateVideoContainer();

        Assert.Throws<InvalidOperationException>(container.Open);
    }

    [SkippableFact]
    public void Open_AfterInitialize_VideoSource_SetsIsOpenAndCreatesVideoComponent()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        using var container = CreateVideoContainer();
        container.Initialize();
        container.Open();

        Assert.True(container.IsOpen);
        Assert.True(container.Components.HasVideo);
    }

    [SkippableFact]
    public void Open_AfterInitialize_AudioSource_SetsIsOpenAndCreatesAudioComponent()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        using var container = CreateAudioContainer();
        container.Initialize();
        container.Open();

        Assert.True(container.IsOpen);
        Assert.True(container.Components.HasAudio);
    }

    [SkippableFact]
    public void Open_CalledTwice_ThrowsInvalidOperationException()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        using var container = CreateVideoContainer();
        container.Initialize();
        container.Open();

        Assert.Throws<InvalidOperationException>(container.Open);
    }

    // -------------------------------------------------------------------------
    // Read
    // -------------------------------------------------------------------------

    [SkippableFact]
    public void Read_BeforeInitialize_ThrowsInvalidOperationException()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        using var container = CreateVideoContainer();

        Assert.Throws<InvalidOperationException>(() => container.Read());
    }

    [SkippableFact]
    public void Read_AfterInitializeBeforeOpen_ThrowsInvalidOperationException()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        using var container = CreateVideoContainer();
        container.Initialize();

        Assert.Throws<InvalidOperationException>(() => container.Read());
    }

    [SkippableFact]
    public void Read_AfterOpen_ReturnsVideoMediaType()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        using var container = CreateVideoContainer();
        container.Initialize();
        container.Open();

        var mediaType = container.Read();

        Assert.Equal(MediaType.Video, mediaType);
    }

    [SkippableFact]
    public void Read_AfterDispose_ThrowsObjectDisposedException()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        var container = CreateVideoContainer();
        container.Initialize();
        container.Open();
        container.Dispose();

        Assert.Throws<ObjectDisposedException>(() => container.Read());
    }

    // -------------------------------------------------------------------------
    // Decode
    // -------------------------------------------------------------------------

    [SkippableFact]
    public void Decode_BeforeInitialize_ThrowsInvalidOperationException()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        using var container = CreateVideoContainer();

        Assert.Throws<InvalidOperationException>(container.Decode);
    }

    [SkippableFact]
    public void Decode_AfterInitializeBeforeOpen_ReturnsEmptyList()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        using var container = CreateVideoContainer();
        container.Initialize();

        // No components are open yet, so Decode silently returns nothing.
        var frames = container.Decode();
        Assert.Empty(frames);
    }

    [SkippableFact]
    public void Decode_AfterRead_ReturnsVideoFrames()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        using var container = CreateVideoContainer();
        container.Initialize();
        container.Open();

        var frames = ReadUntilFrames(container);
        try
        {
            Assert.NotEmpty(frames);
            Assert.All(frames, f => Assert.Equal(MediaType.Video, f.MediaType));
        }
        finally
        {
            foreach (var f in frames) f.Dispose();
        }
    }

    [SkippableFact]
    public void Decode_AfterDispose_ThrowsObjectDisposedException()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        var container = CreateVideoContainer();
        container.Initialize();
        container.Open();
        container.Dispose();

        Assert.Throws<ObjectDisposedException>(container.Decode);
    }

    // -------------------------------------------------------------------------
    // Convert
    // -------------------------------------------------------------------------

    [SkippableFact]
    public void Convert_VideoFrame_ProducesVideoBlockWithCorrectDimensions()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        using var container = CreateVideoContainer();
        container.Initialize();
        container.Open();

        MediaBlock? block = null;
        try
        {
            block = ConvertFirstFrame(container, MediaType.Video);
            Skip.If(block is null, "No video frame produced by lavfi source.");

            var videoBlock = Assert.IsType<VideoBlock>(block);
            Assert.Equal(64, videoBlock.PixelWidth);
            Assert.Equal(48, videoBlock.PixelHeight);
            Assert.True(videoBlock.IsAllocated);
            Assert.True(videoBlock.BufferLength > 0);
            Assert.Equal(MediaType.Video, videoBlock.MediaType);
        }
        finally
        {
            block?.Dispose();
        }
    }

    [SkippableFact]
    public void Convert_AudioFrame_ProducesAudioBlockWithCorrectProperties()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        using var container = CreateAudioContainer();
        container.Initialize();
        container.Open();

        MediaBlock? block = null;
        try
        {
            block = ConvertFirstFrame(container, MediaType.Audio);
            Skip.If(block is null, "No audio frame produced by lavfi source.");

            var audioBlock = Assert.IsType<AudioBlock>(block);
            Assert.True(audioBlock.SampleRate > 0);
            Assert.True(audioBlock.ChannelCount >= 1);
            Assert.True(audioBlock.SamplesPerChannel > 0);
            Assert.Equal(MediaType.Audio, audioBlock.MediaType);
        }
        finally
        {
            block?.Dispose();
        }
    }

    [SkippableFact]
    public void Convert_AfterDispose_ThrowsObjectDisposedException()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        var container = CreateVideoContainer();
        container.Initialize();
        container.Dispose();

        // IsDisposed is checked before the null-input guard, so null is safe to pass here.
        MediaBlock? block = null;
        Assert.Throws<ObjectDisposedException>(() =>
            container.Convert(null!, ref block, releaseInput: false, previousBlock: null));
    }

    [SkippableFact]
    public void Convert_WithStaleFrame_ThrowsArgumentException()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        using var container = CreateVideoContainer();
        container.Initialize();
        container.Open();

        var frames = ReadUntilFrames(container);
        Skip.If(frames.Count == 0, "No frames produced by lavfi source.");

        var staleFrame = frames[0];
        foreach (var f in frames) f.Dispose(); // disposes frames[0], making it stale

        MediaBlock? block = null;
        Assert.Throws<ArgumentException>(() =>
            container.Convert(staleFrame, ref block, releaseInput: false, previousBlock: null));
    }

    [SkippableFact]
    public void Convert_WithNullInput_ThrowsArgumentNullException()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        using var container = CreateVideoContainer();
        container.Initialize();
        container.Open();

        MediaBlock? block = null;
        Assert.Throws<ArgumentNullException>(() =>
            container.Convert(null!, ref block, releaseInput: false, previousBlock: null));
    }

    // -------------------------------------------------------------------------
    // Full pipeline
    // -------------------------------------------------------------------------

    [SkippableFact]
    public void FullPipeline_VideoSource_ExhaustsStreamAndProducesBlocks()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        using var container = CreateVideoContainer();
        container.Initialize();
        container.Open();

        var blockCount = 0;
        MediaBlock? block = null;

        // 2-second @ 1 fps source produces 2 frames; guard against infinite sources
        for (var guard = 0; !container.IsAtEndOfStream && guard < 200; guard++)
        {
            container.Read();
            foreach (var frame in container.Decode())
            {
                container.Convert(frame, ref block, releaseInput: true, previousBlock: null);
                if (block is not null) blockCount++;
            }
        }

        block?.Dispose();

        Assert.True(container.IsAtEndOfStream, "Stream should have reached end-of-file.");
        Assert.True(blockCount > 0, "Expected at least one decoded block from a 2-second source.");
    }

    // -------------------------------------------------------------------------
    // Seek
    // -------------------------------------------------------------------------

    [SkippableFact]
    public void Seek_BeforeInitialize_ThrowsInvalidOperationException()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        using var container = CreateVideoContainer();

        Assert.Throws<InvalidOperationException>(() => container.Seek(TimeSpan.Zero));
    }

    [SkippableFact]
    public void Seek_AfterDispose_ThrowsObjectDisposedException()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        var container = CreateVideoContainer();
        container.Initialize();
        container.Dispose();

        Assert.Throws<ObjectDisposedException>(() => container.Seek(TimeSpan.Zero));
    }

    [SkippableFact]
    public void Seek_ToMiddleOfStream_ReturnsFrameAtOrBeforeTarget()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        using var container = CreateVideoContainer();
        container.Initialize();
        container.Open();

        var target = TimeSpan.FromSeconds(1);
        var frame = container.Seek(target);

        // lavfi sources may not be seekable depending on the OS/build
        Skip.If(frame is null, "lavfi source is not seekable; skipping seek test.");

        try
        {
            Assert.True(frame.StartTime <= target,
                $"Expected frame at or before {target} but got StartTime={frame.StartTime}.");
        }
        finally
        {
            frame.Dispose();
        }
    }

    // -------------------------------------------------------------------------
    // Dispose
    // -------------------------------------------------------------------------

    [SkippableFact]
    public void Dispose_AfterOpen_SetsIsDisposedAndClosesContainer()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        var container = CreateVideoContainer();
        container.Initialize();
        container.Open();

        container.Dispose();

        Assert.True(container.IsDisposed);
        Assert.False(container.IsOpen);
        Assert.False(container.IsInitialized);
    }

    [SkippableFact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        Skip.IfNot(_ffmpeg.IsAvailable, SkipReason);

        var container = CreateVideoContainer();
        container.Initialize();

        container.Dispose();

        Assert.Null(Record.Exception(container.Dispose));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private const string SkipReason =
        $"Set {FfmpegFixture.FfmpegDirEnvVar} to the FFmpeg binary directory to run these tests.";

    /// <summary>
    /// 2-second, 64×48, 1 fps video test pattern via FFmpeg's lavfi virtual device.
    /// No media file required; just the FFmpeg binaries.
    /// </summary>
    private static MediaContainer CreateVideoContainer()
    {
        var config = new ContainerConfiguration { ForcedInputFormat = "lavfi" };
        return new MediaContainer("testsrc=duration=2:size=64x48:rate=1", config, null);
    }

    /// <summary>
    /// 1-second, 440 Hz sine tone via FFmpeg's lavfi virtual device.
    /// </summary>
    private static MediaContainer CreateAudioContainer()
    {
        var config = new ContainerConfiguration { ForcedInputFormat = "lavfi" };
        return new MediaContainer("sine=frequency=440:duration=1", config, null);
    }

    /// <summary>
    /// Calls Read+Decode in a loop until at least one frame is returned or the limit is reached.
    /// Caller is responsible for disposing the returned frames.
    /// </summary>
    private static IList<MediaFrame> ReadUntilFrames(MediaContainer container, int maxReads = 20)
    {
        for (var i = 0; i < maxReads; i++)
        {
            container.Read();
            var frames = container.Decode();
            if (frames.Count > 0)
                return frames;
        }

        return [];
    }

    /// <summary>
    /// Reads and decodes until a frame of the specified type is available, converts it to
    /// a block, and returns the block. The frame is released by Convert. Returns null if
    /// no matching frame was produced within the attempt limit.
    /// </summary>
    private static MediaBlock? ConvertFirstFrame(MediaContainer container, MediaType targetType)
    {
        for (var i = 0; i < 20; i++)
        {
            container.Read();
            foreach (var frame in container.Decode())
            {
                if (frame.MediaType != targetType)
                {
                    frame.Dispose();
                    continue;
                }

                MediaBlock? block = null;
                container.Convert(frame, ref block, releaseInput: true, previousBlock: null);
                return block;
            }
        }

        return null;
    }
}
