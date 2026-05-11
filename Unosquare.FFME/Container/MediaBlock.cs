using FFmpeg.AutoGen;
using System.Threading;

namespace Unosquare.FFME.Container
{
    using Common;
    using Primitives;
    using System;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// A base class for blocks of the different MediaTypes.
    /// Blocks are the result of decoding and scaling a frame.
    /// Blocks have pre-allocated buffers which makes them memory and CPU efficient.
    /// Reuse blocks as much as possible. Once you create a block from a frame,
    /// you don't need the frame anymore so make sure you dispose the frame.
    /// </summary>
    internal abstract class MediaBlock
        : IComparable<MediaBlock>, IComparable<TimeSpan>, IComparable<long>, IEquatable<MediaBlock>, IDisposable
    {
        private readonly Lock _syncLock = new();
        private readonly ISyncLocker _locker = SyncLockerFactory.Create(useSlim: true);
        private volatile bool _isDisposed;
        private IntPtr _buffer = IntPtr.Zero;
        private int _bufferLength;

        /// <summary>
        /// Initializes a new instance of the <see cref="MediaBlock" /> class.
        /// </summary>
        /// <param name="mediaType">Type of the media.</param>
        protected MediaBlock(MediaType mediaType)
        {
            MediaType = mediaType;
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="MediaBlock"/> class.
        /// </summary>
        ~MediaBlock() => Dispose(false);

        /// <summary>
        /// Gets the media type of the data.
        /// </summary>
        public MediaType MediaType { get; }

        /// <summary>
        /// Gets the size of the compressed frame.
        /// </summary>
        public int CompressedSize { get; internal set; }

        /// <summary>
        /// Gets a value indicating whether the start time was guessed from siblings
        /// or the source frame PTS comes from a NO PTS value.
        /// </summary>
        public bool IsStartTimeGuessed { get; internal set; }

        /// <summary>
        /// Gets the time at which this data should be presented (PTS).
        /// </summary>
        public TimeSpan StartTime { get; internal set; }

        /// <summary>
        /// Gets the amount of time this data has to be presented.
        /// </summary>
        public TimeSpan Duration { get; internal set; }

        /// <summary>
        /// Gets the end time.
        /// </summary>
        public TimeSpan EndTime { get; internal set; }

        /// <summary>
        /// Gets the unadjusted, original presentation timestamp (PTS) of the frame given in
        /// the stream's Time Base units.
        /// </summary>
        public long PresentationTime { get; internal set; }

        /// <summary>
        /// Gets the index of the stream.
        /// </summary>
        public int StreamIndex { get; internal set; }

        /// <summary>
        /// Gets a pointer to the first byte of the unmanaged data buffer.
        /// </summary>
        public IntPtr Buffer { get { lock (_syncLock) return _buffer; } }

        /// <summary>
        /// Gets the length of the unmanaged buffer in bytes.
        /// </summary>
        public int BufferLength { get { lock (_syncLock) return _bufferLength; } }

        /// <summary>
        /// Gets a value indicating whether an unmanaged buffer has been allocated.
        /// </summary>
        public bool IsAllocated
        {
            get
            {
                lock (_syncLock)
                    return !IsDisposed && _buffer != IntPtr.Zero;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this block is disposed.
        /// </summary>
        public bool IsDisposed
        {
            get => _isDisposed;
            private set => _isDisposed = value;
        }

        /// <summary>
        /// Gets or sets the index within the block buffer.
        /// </summary>
        internal int Index { get; set; }

        /// <summary>
        /// Gets or sets the next MediaBlock.
        /// </summary>
        internal MediaBlock Next { get; set; }

        /// <summary>
        /// Gets or sets the previous MediaBlock.
        /// </summary>
        internal MediaBlock Previous { get; set; }

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator ==(MediaBlock left, MediaBlock right)
        {
            if (left is null)
                return right is null;

            return left.Equals(right);
        }

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator !=(MediaBlock left, MediaBlock right) =>
            !(left == right);

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator <(MediaBlock left, MediaBlock right) =>
            left == null ? right != null : left.CompareTo(right) < 0;

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator <=(MediaBlock left, MediaBlock right) =>
            left == null || left.CompareTo(right) <= 0;

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator >(MediaBlock left, MediaBlock right) =>
            left != null && left.CompareTo(right) > 0;

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator >=(MediaBlock left, MediaBlock right) =>
            left == null ? right == null : left.CompareTo(right) >= 0;

        /// <summary>
        /// Tries the acquire a reader lock on the unmanaged buffer.
        /// Returns false if the buffer has been disposed.
        /// </summary>
        /// <param name="locker">The locker.</param>
        /// <returns>The disposable lock.</returns>
        public bool TryAcquireReaderLock(out IDisposable locker)
        {
            locker = null;
            lock (_syncLock)
                return !IsDisposed && _locker.TryAcquireReaderLock(out locker);
        }

        /// <summary>
        /// Tries the acquire a writer lock on the unmanaged buffer.
        /// Returns false if the buffer has been disposed or a lock operation times out.
        /// </summary>
        /// <param name="locker">The locker.</param>
        /// <returns>The disposable lock.</returns>
        public bool TryAcquireWriterLock(out IDisposable locker)
        {
            locker = null;
            lock (_syncLock)
                return !IsDisposed && _locker.TryAcquireWriterLock(out locker);
        }

        /// <summary>
        /// Determines whether this media block holds the specified position.
        /// Returns false if it does not have a valid duration.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <returns>
        ///   <c>true</c> if [contains] [the specified position]; otherwise, <c>false</c>.
        /// </returns>
        public bool Contains(TimeSpan position)
        {
            if (!IsDisposed && Duration <= TimeSpan.Zero)
                return false;

            return position.Ticks >= StartTime.Ticks
                && position.Ticks <= EndTime.Ticks;
        }

        /// <inheritdoc />
        public int CompareTo(MediaBlock other)
        {
            ArgumentNullException.ThrowIfNull(other);
            return StartTime.Ticks.CompareTo(other.StartTime.Ticks);
        }

        /// <inheritdoc />
        public int CompareTo(TimeSpan other)
        {
            return StartTime.Ticks.CompareTo(other.Ticks);
        }

        /// <inheritdoc />
        public int CompareTo(long other)
        {
            return StartTime.Ticks.CompareTo(other);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj is MediaBlock other)
                return ReferenceEquals(this, other);

            return false;
        }

        /// <inheritdoc />
        public bool Equals(MediaBlock other) =>
            ReferenceEquals(this, other);

        /// <inheritdoc />
        public override int GetHashCode() =>
            StartTime.Ticks.GetHashCode() ^
            MediaType.GetHashCode();

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Allocates the specified buffer length.
        /// </summary>
        /// <param name="bufferLength">Length of the buffer.</param>
        /// <returns>True if the buffer is successfully allocated.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal virtual unsafe bool Allocate(int bufferLength)
        {
            if (bufferLength <= 0)
                throw new ArgumentException($"{nameof(bufferLength)} must be greater than 0");

            lock (_syncLock)
            {
                if (IsDisposed)
                    return false;

                if (_bufferLength == bufferLength)
                    return true;

                if (!_locker.TryAcquireWriterLock(out var writeLock))
                    return false;

                using (writeLock)
                {
                    Deallocate();
                    _buffer = (IntPtr)ffmpeg.av_malloc((ulong)bufferLength);
                    _bufferLength = bufferLength;
                    return true;
                }
            }
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="alsoManaged"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool alsoManaged)
        {
            lock (_syncLock)
            {
                if (IsDisposed) return;
                IsDisposed = true;

                // Free unmanaged resources (unmanaged objects) and override a finalizer below.
                using (_locker.AcquireWriterLock())
                    Deallocate();

                // set large fields to null.
                if (alsoManaged)
                    _locker.Dispose();
            }
        }

        /// <summary>
        /// De-allocates the picture buffer and resets the related buffer properties.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected virtual unsafe void Deallocate()
        {
            if (_buffer == IntPtr.Zero) return;

            ffmpeg.av_free((void*)_buffer);
            _buffer = IntPtr.Zero;
            _bufferLength = default;
        }
    }
}
