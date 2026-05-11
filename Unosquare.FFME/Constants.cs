using FFmpeg.AutoGen;

namespace Unosquare.FFME
{
    using ClosedCaptions;
    using Common;
    using Engine;
    using System;
    using System.IO;
    using System.Reflection;

    /// <summary>
    /// Defaults and constants of the Media Engine.
    /// </summary>
    internal static class Constants
    {
        /// <summary>
        /// Initializes static members of the <see cref="Constants"/> class.
        /// </summary>
        static Constants()
        {
            try
            {
                var entryAssemblyPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) ?? ".";
                FFmpegSearchPath = Path.GetFullPath(entryAssemblyPath);
                return;
            }
            catch
            {
                // ignore (we might be in winforms design time)
                // see issue #311
            }

            FFmpegSearchPath = ffmpeg.RootPath;
        }

        /// <summary>
        /// Gets the assembly location.
        /// </summary>
        public static string FFmpegSearchPath { get; }

        /// <summary>
        /// The default speed ratio.
        /// </summary>
        public static double DefaultSpeedRatio => 1.0d;

        /// <summary>
        /// The default balance.
        /// </summary>
        public static double DefaultBalance => 0.0d;

        /// <summary>
        /// The default volume.
        /// </summary>
        public static double DefaultVolume => 1.0d;

        /// <summary>
        /// The default closed captions channel.
        /// </summary>
        public static CaptionsChannel DefaultClosedCaptionsChannel => CaptionsChannel.CCP;

        /// <summary>
        /// The minimum speed ratio.
        /// </summary>
        public static double MinSpeedRatio => 0.0d;

        /// <summary>
        /// The maximum speed ratio.
        /// </summary>
        public static double MaxSpeedRatio => 100.0d;

        /// <summary>
        /// The minimum balance.
        /// </summary>
        public static double MinBalance => -1.0d;

        /// <summary>
        /// The maximum balance.
        /// </summary>
        public static double MaxBalance => 1.0d;

        /// <summary>
        /// The maximum volume.
        /// </summary>
        public static double MaxVolume => 1.0d;

        /// <summary>
        /// The minimum volume.
        /// </summary>
        public static double MinVolume => 0.0d;

        /// <summary>
        /// The audio buffer padding.
        /// </summary>
        public static int AudioBufferPadding => 256;

        /// <summary>
        /// The audio bits per sample (1 channel only).
        /// </summary>
        public static int AudioBitsPerSample => 16;

        /// <summary>
        /// The audio bytes per sample.
        /// </summary>
        public static int AudioBytesPerSample => AudioBitsPerSample / 8;

        /// <summary>
        /// The audio sample format.
        /// </summary>
        public static AVSampleFormat AudioSampleFormat => AVSampleFormat.AV_SAMPLE_FMT_S16;

        /// <summary>
        /// The audio channel count.
        /// </summary>
        public static int AudioChannelCount => 2;

        /// <summary>
        /// The audio sample rate (per channel).
        /// </summary>
        public static int AudioSampleRate => 48000;

        /// <summary>
        /// The video bits per component.
        /// </summary>
        public static int VideoBitsPerComponent => 8;

        /// <summary>
        /// The video bits per pixel.
        /// </summary>
        public static int VideoBitsPerPixel => 32;

        /// <summary>
        /// The video bytes per pixel.
        /// </summary>
        public static int VideoBytesPerPixel => 4;

        /// <summary>
        /// The video pixel format. BGRA, 32bit.
        /// </summary>
        public static AVPixelFormat VideoPixelFormat => AVPixelFormat.AV_PIX_FMT_BGRA;

        /// <summary>
        /// Gets the time synchronize maximum offset.
        /// Components that are offset more than this time span with respect to the
        /// main component are deemed unrelated.
        /// </summary>
        internal static TimeSpan TimeSyncMaxOffset { get; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets the period at which media state properties are updated.
        /// </summary>
        internal static TimeSpan PropertyUpdatesInterval { get; } = TimeSpan.FromMilliseconds(30);

        /// <summary>
        /// Gets the timing period for default scenarios.
        /// </summary>
        internal static TimeSpan DefaultTimingPeriod => TimeSpan.FromMilliseconds(15);

        /// <summary>
        /// The minimum video frame duration used to clamp the rendering cycle interval.
        /// Prevents excessively fast polling on very-high-frame-rate content.
        /// </summary>
        internal static TimeSpan MinVideoFrameDuration => TimeSpan.FromMilliseconds(10);

        /// <summary>
        /// The maximum video frame duration used to clamp the rendering cycle interval.
        /// Prevents audio starvation on very-low-frame-rate content.
        /// </summary>
        internal static TimeSpan MaxVideoFrameDuration => TimeSpan.FromMilliseconds(50);

        /// <summary>
        /// Minimum number of video blocks to keep in the block buffer.
        /// </summary>
        internal const int MinVideoBlocks = 8;

        /// <summary>
        /// Minimum number of audio blocks to keep in the block buffer (~1 s at 48 kHz).
        /// </summary>
        internal const int MinAudioBlocks = 48;

        /// <summary>
        /// Minimum number of subtitle blocks to keep in the block buffer.
        /// </summary>
        internal const int MinSubtitleBlocks = 4;

        /// <summary>
        /// Target lower bound of the live-stream packet buffer in milliseconds (work in progress).
        /// </summary>
        internal static double LiveStreamMinBufferMs => 500d;

        /// <summary>
        /// Target upper bound of the live-stream packet buffer in milliseconds (work in progress).
        /// </summary>
        internal static double LiveStreamMaxBufferMs => 1000d;

        /// <summary>
        /// Minimum elapsed time in milliseconds between live-stream speed-ratio adjustments.
        /// </summary>
        internal static double LiveStreamSpeedUpdateTimeoutMs => 100d;

        /// <summary>
        /// Minimum buffer delta in milliseconds before a live-stream timing nudge is applied.
        /// </summary>
        internal static double LiveStreamTimingAdjustThresholdMs => 100d;

        /// <summary>
        /// Maximum bytes-of-buffer change applied per audio A/V sync correction step.
        /// Expressed in milliseconds of audio duration.
        /// </summary>
        internal static double AudioSyncLatencyStepMs => 10d;

        /// <summary>
        /// Minimum elapsed time in milliseconds between consecutive audio skip/rewind corrections.
        /// </summary>
        internal static double AudioSyncUpdateTimeoutMs => 200d;

        /// <summary>
        /// Maximum audio lag (ms) before samples are skipped to re-sync.
        /// Positive values mean audio is behind video.
        /// </summary>
        internal static double AudioSyncMaxLagMs => 0d;

        /// <summary>
        /// Minimum audio lead (ms) before samples are rewound to re-sync.
        /// Derived from <see cref="AudioSyncLatencyStepMs"/>.
        /// </summary>
        internal static double AudioSyncMinLeadMs => -2d * AudioSyncLatencyStepMs;

        /// <summary>
        /// Minimum stream size in bytes below which buffering statistics are not computed.
        /// </summary>
        internal const long MinimumValidFileSize = 1024 * 1024;

        /// <summary>
        /// Gets the maximum blocks to cache for the given component type.
        /// </summary>
        /// <param name="t">The t.</param>
        /// <param name="mediaCore">The media core.</param>
        /// <returns>The number of blocks to cache.</returns>
        internal static int GetMaxBlocks(MediaType t, MediaEngine mediaCore)
        {
            var result = 0;

            if (t == MediaType.Video)
            {
                result = mediaCore.MediaOptions.VideoBlockCache;
                if (result < MinVideoBlocks) result = MinVideoBlocks;
            }
            else if (t == MediaType.Audio)
            {
                result = mediaCore.MediaOptions.AudioBlockCache;
                if (result < MinAudioBlocks) result = MinAudioBlocks;
            }
            else if (t == MediaType.Subtitle)
            {
                result = mediaCore.MediaOptions.SubtitleBlockCache;
                if (result < MinSubtitleBlocks) result = MinSubtitleBlocks;
            }


            return result;
        }
    }
}
