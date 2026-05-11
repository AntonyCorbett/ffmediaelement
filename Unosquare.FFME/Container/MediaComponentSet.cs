using System.Threading;

namespace Unosquare.FFME.Container
{
    using Common;
    using Primitives;
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Represents a set of Audio, Video and Subtitle components.
    /// This class is useful in order to group all components into
    /// a single set. Sending packets is automatically handled by
    /// this class. This class is thread safe.
    /// </summary>
    internal sealed class MediaComponentSet : IDisposable
    {
        #region Private Declarations

        // Synchronization locks
        private readonly Lock _componentSyncLock = new();
        private readonly Lock _bufferSyncLock = new();
        private volatile bool _isDisposed;

        private List<MediaComponent> _all = [];
        private List<MediaType> _mediaTypes = [];

        private int _count;
        private MediaType _seekableMediaType = MediaType.None;
        private MediaComponent _seekable;
        private AudioComponent _audio;
        private VideoComponent _video;
        private SubtitleComponent _subtitle;
        private PacketBufferState _bufferState;

        #endregion

        #region Delegates

        public delegate void OnPacketQueueChangedDelegate(
            PacketQueueOp operation, MediaPacket avPacket, MediaType mediaType, PacketBufferState bufferState);

        public delegate void OnFrameDecodedDelegate(IntPtr avFrame, MediaType mediaType);
        public delegate void OnSubtitleDecodedDelegate(IntPtr avSubtitle);

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets a method that gets called when a packet is queued.
        /// </summary>
        public OnPacketQueueChangedDelegate OnPacketQueueChanged { get; set; }

        /// <summary>
        /// Gets or sets a method that gets called when an audio or video frame gets decoded.
        /// </summary>
        public OnFrameDecodedDelegate OnFrameDecoded { get; set; }

        /// <summary>
        /// Gets or sets a method that gets called when a subtitle frame gets decoded.
        /// </summary>
        public OnSubtitleDecodedDelegate OnSubtitleDecoded { get; set; }

        /// <summary>
        /// Gets a value indicating whether this instance is disposed.
        /// </summary>
        public bool IsDisposed => _isDisposed;

        /// <summary>
        /// Gets the registered component count.
        /// </summary>
        public int Count
        {
            get { lock (_componentSyncLock) return _count; }
        }

        /// <summary>
        /// Gets the available component media types.
        /// </summary>
        public IReadOnlyList<MediaType> MediaTypes
        {
            get { lock (_componentSyncLock) return _mediaTypes; }
        }

        /// <summary>
        /// Gets all the components in a read-only collection.
        /// </summary>
        public IReadOnlyList<MediaComponent> All
        {
            get { lock (_componentSyncLock) return _all; }
        }

        /// <summary>
        /// Gets the type of the component on which seek and frame stepping is performed.
        /// </summary>
        public MediaType SeekableMediaType
        {
            get { lock (_componentSyncLock) return _seekableMediaType; }
        }

        /// <summary>
        /// Gets the media component of the stream on which seeking and frame stepping is performed.
        /// By order of priority, first Video (not containing picture attachments), then audio.
        /// </summary>
        public MediaComponent Seekable
        {
            get { lock (_componentSyncLock) return _seekable; }
        }

        /// <summary>
        /// Gets the video component.
        /// Returns null when there is no such stream component.
        /// </summary>
        public VideoComponent Video
        {
            get { lock (_componentSyncLock) return _video; }
        }

        /// <summary>
        /// Gets the audio component.
        /// Returns null when there is no such stream component.
        /// </summary>
        public AudioComponent Audio
        {
            get { lock (_componentSyncLock) return _audio; }
        }

        /// <summary>
        /// Gets the subtitles component.
        /// Returns null when there is no such stream component.
        /// </summary>
        public SubtitleComponent Subtitles
        {
            get { lock (_componentSyncLock) return _subtitle; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance has a video component.
        /// </summary>
        public bool HasVideo
        {
            get { lock (_componentSyncLock) return _video != null; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance has an audio component.
        /// </summary>
        public bool HasAudio
        {
            get { lock (_componentSyncLock) return _audio != null; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance has a subtitles component.
        /// </summary>
        public bool HasSubtitles
        {
            get { lock (_componentSyncLock) return _subtitle != null; }
        }

        /// <summary>
        /// Gets the current length in bytes of the packet buffer for all components.
        /// These packets are the ones that have not been yet decoded.
        /// </summary>
        public long BufferLength
        {
            get { lock (_bufferSyncLock) return _bufferState.Length; }
        }

        /// <summary>
        /// Gets the total number of packets in the packet buffer for all components.
        /// </summary>
        public int BufferCount
        {
            get { lock (_bufferSyncLock) return _bufferState.Count; }
        }

        /// <summary>
        /// Gets the the least duration between the buffered audio and video packets.
        /// If no duration information is encoded in neither, this property will return
        /// <see cref="TimeSpan.MinValue"/>.
        /// </summary>
        public TimeSpan BufferDuration
        {
            get { lock (_bufferSyncLock) return _bufferState.Duration; }
        }

        /// <summary>
        /// Gets the minimum number of packets to read before <see cref="HasEnoughPackets"/> is able to return true.
        /// </summary>
        public int BufferCountThreshold
        {
            get { lock (_bufferSyncLock) return _bufferState.CountThreshold; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether all packet queues contain enough packets.
        /// Port of ffplay.c stream_has_enough_packets.
        /// </summary>
        public bool HasEnoughPackets
        {
            get { lock (_bufferSyncLock) return _bufferState.HasEnoughPackets; }
        }

        /// <summary>
        /// Gets or sets the <see cref="MediaComponent"/> with the specified media type.
        /// Setting a new component on an existing media type component will throw.
        /// Getting a non existing media component fro the given media type will return null.
        /// </summary>
        /// <param name="mediaType">Type of the media.</param>
        /// <returns>The media component.</returns>
        /// <exception cref="ArgumentException">When the media type is invalid.</exception>
        /// <exception cref="ArgumentNullException">MediaComponent.</exception>
        public MediaComponent this[MediaType mediaType]
        {
            get
            {
                lock (_componentSyncLock)
                {
                    return mediaType switch
                    {
                        MediaType.Audio => _audio,
                        MediaType.Video => _video,
                        MediaType.Subtitle => _subtitle,
                        _ => null,
                    };
                }
            }
        }

        /// <inheritdoc />
        public void Dispose() => Dispose(true);

        #endregion

        #region Methods

        /// <summary>
        /// Sends the specified packet to the correct component by reading the stream index
        /// of the packet that is being sent. No packet is sent if the provided packet is set to null.
        /// Returns the media type of the component that accepted the packet.
        /// </summary>
        /// <param name="packet">The packet.</param>
        /// <returns>The media type.</returns>
        public MediaType SendPacket(MediaPacket packet)
        {
            if (packet == null)
                return MediaType.None;

            foreach (var component in All)
            {
                if (component.StreamIndex != packet.StreamIndex)
                    continue;

                component.SendPacket(packet);
                return component.MediaType;
            }

            return MediaType.None;
        }

        /// <summary>
        /// Sends an empty packet to all media components.
        /// When an EOF/EOS situation is encountered, this forces
        /// the decoders to enter draining mode until all frames are decoded.
        /// </summary>
        public void SendEmptyPackets()
        {
            foreach (var component in All)
                component.SendEmptyPacket();
        }

        /// <summary>
        /// Clears the packet queues for all components.
        /// Additionally it flushes the codec buffered packets.
        /// This is useful after a seek operation is performed or a stream
        /// index is changed.
        /// </summary>
        /// <param name="flushBuffers">if set to <c>true</c> flush codec buffers.</param>
        public void ClearQueuedPackets(bool flushBuffers)
        {
            foreach (var component in All)
                component.ClearQueuedPackets(flushBuffers);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Updates queue properties and invokes the on packet queue changed callback.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="packet">The packet.</param>
        /// <param name="mediaType">Type of the media.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ProcessPacketQueueChanges(PacketQueueOp operation, MediaPacket packet, MediaType mediaType)
        {
            if (OnPacketQueueChanged == null)
                return;

            var state = default(PacketBufferState);
            state.HasEnoughPackets = true;
            state.Duration = TimeSpan.MaxValue;

            foreach (var c in All)
            {
                state.Length += c.BufferLength;
                state.Count += c.BufferCount;
                state.CountThreshold += c.BufferCountThreshold;
                if (c.HasEnoughPackets == false)
                    state.HasEnoughPackets = false;

                if ((c.MediaType == MediaType.Audio || c.MediaType == MediaType.Video) &&
                    c.BufferDuration != TimeSpan.MinValue &&
                    c.BufferDuration.Ticks < state.Duration.Ticks)
                {
                    state.Duration = c.BufferDuration;
                }
            }

            if (state.Duration == TimeSpan.MaxValue)
                state.Duration = TimeSpan.MinValue;

            // Update the buffer state
            lock (_bufferSyncLock)
                _bufferState = state;

            // Send the callback
            OnPacketQueueChanged?.Invoke(operation, packet, mediaType, state);
        }

        /// <summary>
        /// Registers the component in this component set.
        /// </summary>
        /// <param name="component">The component.</param>
        /// <exception cref="ArgumentNullException">When component of the same type is already registered.</exception>
        /// <exception cref="NotSupportedException">When MediaType is not supported.</exception>
        /// <exception cref="ArgumentException">When the component is null.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AddComponent(MediaComponent component)
        {
            lock (_componentSyncLock)
            {
                ArgumentNullException.ThrowIfNull(component);

                var errorMessage = $"A component for '{component.MediaType}' is already registered.";
                switch (component.MediaType)
                {
                    case MediaType.Audio:
                        if (_audio != null)
                            throw new ArgumentException(errorMessage);

                        _audio = component as AudioComponent;
                        break;
                    case MediaType.Video:
                        if (_video != null)
                            throw new ArgumentException(errorMessage);

                        _video = component as VideoComponent;
                        break;
                    case MediaType.Subtitle:
                        if (_subtitle != null)
                            throw new ArgumentException(errorMessage);

                        _subtitle = component as SubtitleComponent;
                        break;
                    default:
                        throw new NotSupportedException($"Unable to register component with {nameof(MediaType)} '{component.MediaType}'");
                }

                UpdateComponentBackingFields();
            }
        }

        /// <summary>
        /// Removes the component of specified media type (if registered).
        /// It calls the dispose method of the media component too.
        /// </summary>
        /// <param name="mediaType">Type of the media.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RemoveComponent(MediaType mediaType)
        {
            lock (_componentSyncLock)
            {
                var component = default(MediaComponent);
                if (mediaType == MediaType.Audio)
                {
                    component = _audio;
                    _audio = null;
                }
                else if (mediaType == MediaType.Video)
                {
                    component = _video;
                    _video = null;
                }
                else if (mediaType == MediaType.Subtitle)
                {
                    component = _subtitle;
                    _subtitle = null;
                }

                component?.Dispose();
                UpdateComponentBackingFields();
            }
        }

        /// <summary>
        /// Computes the main component and backing fields.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void UpdateComponentBackingFields()
        {
            var allComponents = new List<MediaComponent>(4);
            var allMediaTypes = new List<MediaType>(4);

            // assign allMediaTypes. IMPORTANT: Order matters because this
            // establishes the priority in which playback measures are computed
            if (_video != null)
            {
                allComponents.Add(_video);
                allMediaTypes.Add(MediaType.Video);
            }

            if (_audio != null)
            {
                allComponents.Add(_audio);
                allMediaTypes.Add(MediaType.Audio);
            }

            if (_subtitle != null)
            {
                allComponents.Add(_subtitle);
                allMediaTypes.Add(MediaType.Subtitle);
            }

            _all = allComponents;
            _mediaTypes = allMediaTypes;
            _count = allComponents.Count;

            // Try for the main component to be the video (if it's not stuff like audio album art, that is)
            if (_video != null && _audio != null && !_video.IsStillPictures)
            {
                _seekable = _video;
                _seekableMediaType = MediaType.Video;
                return;
            }

            // If it was not video, then it has to be audio (if it has audio)
            if (_audio != null)
            {
                _seekable = _audio;
                _seekableMediaType = MediaType.Audio;
                return;
            }

            // Set it to video even if it's attached pic stuff
            if (_video != null)
            {
                _seekable = _video;
                _seekableMediaType = MediaType.Video;
                return;
            }

            // As a last resort, set the main component to be the subtitles
            if (_subtitle != null)
            {
                _seekable = _subtitle;
                _seekableMediaType = MediaType.Subtitle;
                return;
            }

            // We should never really hit this line
            _seekable = null;
            _seekableMediaType = MediaType.None;
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="alsoManaged"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        private void Dispose(bool alsoManaged)
        {
            lock (_componentSyncLock)
            {
                if (IsDisposed || alsoManaged == false)
                    return;

                _isDisposed = true;
                foreach (var mediaType in _mediaTypes)
                    RemoveComponent(mediaType);
            }
        }
    }

    #endregion
}
