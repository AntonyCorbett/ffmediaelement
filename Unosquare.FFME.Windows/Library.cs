extern alias Core;

namespace Unosquare.FFME
{
    using Common;
    using Rendering.Wave;
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Runtime.CompilerServices;
    using System.Threading.Tasks;
    using System.Windows;

    using CoreLibrary = Core::Unosquare.FFME.FFmpegLibrary;

    /// <summary>
    /// Provides access to the underlying FFmpeg library information.
    /// </summary>
    public static class Library
    {
        #region Core Forwarding

        /// <summary>
        /// Gets or sets the FFmpeg path from which to load the FFmpeg binaries.
        /// You must set this path before setting the Source property for the first time on any instance of this control.
        /// Setting this property when FFmpeg binaries have been registered will have no effect.
        /// </summary>
        public static string FFmpegDirectory
        {
            get => CoreLibrary.FFmpegDirectory;
            set => CoreLibrary.FFmpegDirectory = value;
        }

        /// <summary>
        /// Gets the FFmpeg version information. Returns null when the libraries have not been loaded.
        /// </summary>
        public static string FFmpegVersionInfo => CoreLibrary.FFmpegVersionInfo;

        /// <summary>
        /// Gets or sets the FFmpeg log level.
        /// </summary>
        public static int FFmpegLogLevel
        {
            get => CoreLibrary.FFmpegLogLevel;
            set => CoreLibrary.FFmpegLogLevel = value;
        }

        /// <summary>
        /// Gets a value indicating whether the FFmpeg library has been initialized.
        /// </summary>
        public static bool IsInitialized => CoreLibrary.IsInitialized;

        /// <summary>
        /// Gets the registered FFmpeg input format names.
        /// </summary>
        public static IReadOnlyList<string> InputFormatNames => CoreLibrary.InputFormatNames;

        /// <summary>
        /// Gets the global input format options information.
        /// </summary>
        public static IReadOnlyList<OptionMetadata> InputFormatOptionsGlobal => CoreLibrary.InputFormatOptionsGlobal;

        /// <summary>
        /// Gets the input format options.
        /// </summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<OptionMetadata>> InputFormatOptions => CoreLibrary.InputFormatOptions;

        /// <summary>
        /// Gets the registered FFmpeg decoder codec names.
        /// </summary>
        public static IReadOnlyList<string> DecoderNames => CoreLibrary.DecoderNames;

        /// <summary>
        /// Gets the registered FFmpeg encoder codec names.
        /// </summary>
        public static IReadOnlyList<string> EncoderNames => CoreLibrary.EncoderNames;

        /// <summary>
        /// Gets the global options that apply to all decoders.
        /// </summary>
        public static IReadOnlyList<OptionMetadata> DecoderOptionsGlobal => CoreLibrary.DecoderOptionsGlobal;

        /// <summary>
        /// Gets the decoder specific options.
        /// </summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<OptionMetadata>> DecoderOptions => CoreLibrary.DecoderOptions;

        /// <summary>
        /// Forces the loading of the FFmpeg libraries according to the values of <see cref="FFmpegDirectory"/>.
        /// Also, sets the <see cref="FFmpegVersionInfo"/> property. Throws an exception if the libraries cannot be loaded.
        /// </summary>
        /// <returns>true if libraries were loaded, false if libraries were already loaded.</returns>
        public static bool LoadFFmpeg() => CoreLibrary.LoadFFmpeg();

        /// <summary>
        /// Provides an asynchronous version of the <see cref="LoadFFmpeg"/> call.
        /// </summary>
        /// <returns>true if libraries were loaded, false if libraries were already loaded.</returns>
        public static ConfiguredTaskAwaitable<bool> LoadFFmpegAsync() => CoreLibrary.LoadFFmpegAsync();

        /// <summary>
        /// Unloads FFmpeg libraries from memory.
        /// </summary>
        public static void UnloadFFmpeg() => CoreLibrary.UnloadFFmpeg();

        /// <summary>
        /// Retrieves the media information including all streams, chapters and programs.
        /// </summary>
        /// <param name="mediaSource">The source URL.</param>
        /// <returns>The contents of the media information.</returns>
        public static MediaInfo RetrieveMediaInfo(string mediaSource) => CoreLibrary.RetrieveMediaInfo(mediaSource);

        /// <summary>
        /// Creates a video seek index object by decoding video frames and obtaining the intra-frames valid for index positions.
        /// </summary>
        /// <param name="mediaSource">The source URL.</param>
        /// <param name="streamIndex">Index of the stream. Use -1 for automatic stream selection.</param>
        /// <returns>The seek index object.</returns>
        public static VideoSeekIndex CreateVideoSeekIndex(string mediaSource, int streamIndex) =>
            CoreLibrary.CreateVideoSeekIndex(mediaSource, streamIndex);

        /// <summary>
        /// Creates a video seek index object of the default video stream.
        /// </summary>
        /// <param name="mediaSource">The source URL.</param>
        /// <returns>The seek index object.</returns>
        public static VideoSeekIndex CreateVideoSeekIndex(string mediaSource) => CoreLibrary.CreateVideoSeekIndex(mediaSource);

        #endregion

        #region WPF Extensions

        /// <summary>
        /// Gets or sets a value indicating whether the video visualization control
        /// creates its own dispatcher thread to handle rendering of video frames.
        /// This is an experimental feature and it is useful when creating video walls.
        /// For example if you want to display multiple videos at a time and don't want to
        /// use time from the main UI thread. This feature is only valid if we are in
        /// a WPF context.
        /// </summary>
        public static bool EnableWpfMultiThreadedVideo { get; set; }

        /// <summary>
        /// The default DirectSound device.
        /// </summary>
        public static DirectSoundDeviceInfo DefaultDirectSoundDevice { get; } = new DirectSoundDeviceInfo(
            DirectSoundPlayer.DefaultPlaybackDeviceId, nameof(DefaultDirectSoundDevice), nameof(DirectSoundPlayer), true, Guid.Empty.ToString());

        /// <summary>
        /// The default Windows Multimedia Extensions Legacy Audio Device.
        /// </summary>
        public static LegacyAudioDeviceInfo DefaultLegacyAudioDevice { get; } = new LegacyAudioDeviceInfo(
            -1, nameof(DefaultLegacyAudioDevice), nameof(LegacyAudioPlayer), true, Guid.Empty.ToString());

        /// <summary>
        /// Determines if the control library is currently in design-time mode (as opposed to run-time).
        /// </summary>
        internal static bool IsInDesignMode =>
            (bool)DesignerProperties.IsInDesignModeProperty.GetMetadata(typeof(DependencyObject)).DefaultValue;

        /// <summary>
        /// Enumerates the DirectSound devices.
        /// </summary>
        /// <returns>The available DirectSound devices.</returns>
        public static IEnumerable<DirectSoundDeviceInfo> EnumerateDirectSoundDevices()
        {
            var devices = DirectSoundPlayer.EnumerateDevices();
            var result = new List<DirectSoundDeviceInfo>(16) { DefaultDirectSoundDevice };

            foreach (var device in devices)
            {
                result.Add(new DirectSoundDeviceInfo(
                    device.Guid, device.Description, nameof(DirectSoundPlayer), false, device.ModuleName));
            }

            return result;
        }

        /// <summary>
        /// Enumerates the (Legacy) Windows Multimedia Extensions devices.
        /// </summary>
        /// <returns>The available MME devices.</returns>
        public static IEnumerable<LegacyAudioDeviceInfo> EnumerateLegacyAudioDevices()
        {
            var devices = LegacyAudioPlayer.EnumerateDevices();
            var result = new List<LegacyAudioDeviceInfo>(16) { DefaultLegacyAudioDevice };

            for (var deviceId = 0; deviceId < devices.Count; deviceId++)
            {
                var device = devices[deviceId];
                result.Add(new LegacyAudioDeviceInfo(
                    deviceId, device.ProductName, nameof(LegacyAudioPlayer), false, device.ProductGuid.ToString()));
            }

            return result;
        }

        #endregion
    }
}
