using System.Threading;

namespace Unosquare.FFME.Container
{
    using Common;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Represents a set of pre-allocated media blocks of the same media type.
    /// A block buffer contains playback and pool blocks. Pool blocks are blocks that
    /// can be reused. Playback blocks are blocks that have been filled.
    /// This class is thread safe.
    /// </summary>
    internal sealed class MediaBlockBuffer : IDisposable
    {
        #region Private Declarations

        /// <summary>
        /// The blocks that are available to be filled.
        /// </summary>
        private readonly Queue<MediaBlock> _poolBlocks;

        /// <summary>
        /// The blocks that are available for rendering.
        /// </summary>
        private readonly List<MediaBlock> _playbackBlocks;

        /// <summary>
        /// Controls multiple reads and exclusive writes.
        /// </summary>
        private readonly Lock _syncLock = new();

        private bool _isNonMonotonic;
        private TimeSpan _rangeStartTime;
        private TimeSpan _rangeEndTime;
        private TimeSpan _rangeMidTime;
        private TimeSpan _rangeDuration;
        private TimeSpan _averageBlockDuration;
        private TimeSpan _monotonicDuration;
        private int _count;
        private long _rangeBitRate;
        private double _capacityPercent;
        private bool _isMonotonic;
        private bool _isFull;
        private bool _isDisposed;

        // Fast Last Lookup.
        private long _lastLookupTimeTicks = TimeSpan.MinValue.Ticks;
        private int _lastLookupIndex = -1;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="MediaBlockBuffer"/> class.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        /// <param name="mediaType">Type of the media.</param>
        public MediaBlockBuffer(int capacity, MediaType mediaType)
        {
            Capacity = capacity;
            MediaType = mediaType;
            _poolBlocks = new Queue<MediaBlock>(capacity + 1); // +1 to be safe and not degrade performance

#pragma warning disable IDE0028 // don't simplify init since we want to set the initial capacity.
            _playbackBlocks = new List<MediaBlock>(capacity + 1); // +1 to be safe and not degrade performance
#pragma warning restore IDE0028

            // allocate the blocks
            for (var i = 0; i < capacity; i++)
                _poolBlocks.Enqueue(CreateBlock(mediaType));
        }

        #endregion

        #region Regular Properties

        /// <summary>
        /// Gets the media type of the block buffer.
        /// </summary>
        public MediaType MediaType { get; }

        /// <summary>
        /// Gets the maximum count of this buffer.
        /// </summary>
        public int Capacity { get; }

        /// <summary>
        /// Gets a value indicating whether this instance is disposed.
        /// </summary>
        public bool IsDisposed { get { lock (_syncLock) return _isDisposed; } }

        #endregion

        #region Collection Discrete Properties

        /// <summary>
        /// Gets the start time of the first block.
        /// </summary>
        public TimeSpan RangeStartTime { get { lock (_syncLock) return _rangeStartTime; } }

        /// <summary>
        /// Gets the middle time of the range.
        /// </summary>
        public TimeSpan RangeMidTime { get { lock (_syncLock) return _rangeMidTime; } }

        /// <summary>
        /// Gets the end time of the last block.
        /// </summary>
        public TimeSpan RangeEndTime { get { lock (_syncLock) return _rangeEndTime; } }

        /// <summary>
        /// Gets the range of time between the first block and the end time of the last block.
        /// </summary>
        public TimeSpan RangeDuration { get { lock (_syncLock) return _rangeDuration; } }

        /// <summary>
        /// Gets the compressed data bit rate from which media blocks were created.
        /// </summary>
        public long RangeBitRate { get { lock (_syncLock) return _rangeBitRate; } }

        /// <summary>
        /// Gets the average duration of the currently available playback blocks.
        /// </summary>
        public TimeSpan AverageBlockDuration { get { lock (_syncLock) return _averageBlockDuration; } }

        /// <summary>
        /// Gets a value indicating whether all the durations of the blocks are equal.
        /// </summary>
        public bool IsMonotonic { get { lock (_syncLock) return _isMonotonic; } }

        /// <summary>
        /// Gets the duration of the blocks. If the blocks are not monotonic returns zero.
        /// </summary>
        public TimeSpan MonotonicDuration { get { lock (_syncLock) return _monotonicDuration; } }

        /// <summary>
        /// Gets the number of available playback blocks.
        /// </summary>
        public int Count { get { lock (_syncLock) return _count; } }

        /// <summary>
        /// Gets the usage percent from 0.0 to 1.0.
        /// </summary>
        public double CapacityPercent { get { lock (_syncLock) return _capacityPercent; } }

        /// <summary>
        /// Gets a value indicating whether the playback blocks are all allocated.
        /// </summary>
        public bool IsFull { get { lock (_syncLock) return _isFull; } }

        #endregion

        #region Indexer Properties

        /// <summary>
        /// Gets the <see cref="MediaBlock" /> at the specified index.
        /// </summary>
        /// <value>
        /// The <see cref="MediaBlock"/>.
        /// </value>
        /// <param name="index">The index.</param>
        /// <returns>The media block.</returns>
        public MediaBlock this[int index]
        {
            get { lock (_syncLock) return _playbackBlocks[index]; }
        }

        /// <summary>
        /// Gets the <see cref="MediaBlock" /> at the specified timestamp.
        /// </summary>
        /// <value>
        /// The <see cref="MediaBlock"/>.
        /// </value>
        /// <param name="positionTicks">The position to lookup.</param>
        /// <returns>The media block.</returns>
        public MediaBlock this[long positionTicks]
        {
            get
            {
                lock (_syncLock)
                {
                    var index = IndexOf(positionTicks);
                    return index >= 0 ? _playbackBlocks[index] : null;
                }
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets the percentage of the range for the given time position.
        /// A value of less than 0 means the position is behind (lagging).
        /// A value of more than 1 means the position is beyond the range).
        /// </summary>
        /// <param name="position">The position.</param>
        /// <returns>The percent of the range.</returns>
        public double GetRangePercent(TimeSpan position)
        {
            lock (_syncLock)
            {
                return RangeDuration.Ticks != 0 ?
                    Convert.ToDouble(position.Ticks - RangeStartTime.Ticks) / RangeDuration.Ticks : 0d;
            }
        }

        /// <summary>
        /// Gets the neighboring blocks in an atomic operation.
        /// The first item in the array is the previous block. The second is the next block. The third is the current block.
        /// </summary>
        /// <param name="current">The current block to get neighbors from.</param>
        /// <returns>The previous (if any) and next (if any) blocks.</returns>
        public MediaBlock[] Neighbors(MediaBlock current)
        {
            lock (_syncLock)
            {
                var result = new MediaBlock[3];
                if (current == null) return result;

                result[0] = current.Previous;
                result[1] = current.Next;
                result[2] = current;

                return result;
            }
        }

        /// <summary>
        /// Gets the neighboring blocks in an atomic operation.
        /// The first item in the array is the previous block. The second is the next block. The third is the current block.
        /// </summary>
        /// <param name="position">The current block position to get neighbors from.</param>
        /// <returns>The previous (if any) and next (if any) blocks.</returns>
        public MediaBlock[] Neighbors(TimeSpan position)
        {
            lock (_syncLock)
            {
                var current = this[position.Ticks];
                return Neighbors(current);
            }
        }

        /// <summary>
        /// Retrieves the block following the provided current block.
        /// If the argument is null and there are blocks, the first block is returned.
        /// </summary>
        /// <param name="current">The current block.</param>
        /// <returns>The next media block.</returns>
        public MediaBlock Next(MediaBlock current)
        {
            if (current == null) return null;

            lock (_syncLock)
                return current.Next;
        }

        /// <summary>
        /// Retrieves the next time-continuous block.
        /// </summary>
        /// <param name="current">The current.</param>
        /// <returns>The next time-continuous block.</returns>
        public MediaBlock ContinuousNext(MediaBlock current)
        {
            if (current == null) return null;
            lock (_syncLock)
            {
                // capture the next frame
                var next = current.Next;
                if (next == null) return null;

                // capture the spacing between the current and the next frame
                var discontinuity = TimeSpan.FromTicks(
                    next.StartTime.Ticks - current.EndTime.Ticks);

                // return null if we have a discontinuity of more than half of the duration
                var discontinuityThreshold = IsMonotonic ?
                    TimeSpan.FromTicks(current.Duration.Ticks / 2) :
                    TimeSpan.FromMilliseconds(1);

                return discontinuity.Ticks > discontinuityThreshold.Ticks ? null : next;
            }
        }

        /// <summary>
        /// Retrieves the block prior the provided current block.
        /// If the argument is null and there are blocks, the last block is returned.
        /// </summary>
        /// <param name="current">The current block.</param>
        /// <returns>The next media block.</returns>
        public MediaBlock Previous(MediaBlock current)
        {
            if (current == null) return null;

            lock (_syncLock)
                return current.Previous;
        }

        /// <summary>
        /// Determines whether the given render time is within the range of playback blocks.
        /// </summary>
        /// <param name="renderTime">The render time.</param>
        /// <returns>
        ///   <c>true</c> if [is in range] [the specified render time]; otherwise, <c>false</c>.
        /// </returns>
        public bool IsInRange(TimeSpan renderTime)
        {
            lock (_syncLock)
            {
                if (_playbackBlocks.Count == 0) return false;
                return renderTime.Ticks >= RangeStartTime.Ticks && renderTime.Ticks <= RangeEndTime.Ticks;
            }
        }

        /// <summary>
        /// Retrieves the index of the playback block corresponding to the specified
        /// render time. This uses very fast binary and linear search combinations.
        /// If there are no playback blocks it returns -1.
        /// If the render time is greater than the range end time, it returns the last playback block index.
        /// If the render time is less than the range start time, it returns the first playback block index.
        /// </summary>
        /// <param name="renderTimeTicks">The render time.</param>
        /// <returns>The media block's index.</returns>
        public int IndexOf(long renderTimeTicks)
        {
            lock (_syncLock)
            {
                if (_lastLookupTimeTicks != TimeSpan.MinValue.Ticks && renderTimeTicks == _lastLookupTimeTicks)
                    return _lastLookupIndex;

                _lastLookupTimeTicks = renderTimeTicks;
                _lastLookupIndex = _playbackBlocks.Count > 0 && renderTimeTicks <= _playbackBlocks[0].StartTime.Ticks ? 0 :
                    _playbackBlocks.StartIndexOf(_lastLookupTimeTicks);

                return _lastLookupIndex;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (_syncLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                while (_poolBlocks.Count > 0)
                {
                    var block = _poolBlocks.Dequeue();
                    block.Dispose();
                }

                for (var i = _playbackBlocks.Count - 1; i >= 0; i--)
                {
                    var block = _playbackBlocks[i];
                    _playbackBlocks.RemoveAt(i);
                    block.Dispose();
                }

                UpdateCollectionProperties();
            }
        }

        /// <summary>
        /// Adds a block to the playback blocks by converting the given frame.
        /// If there are no more blocks in the pool, the oldest block is returned to the pool
        /// and reused for the new block. The source frame is automatically disposed.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="container">The container.</param>
        /// <returns>The filled block.</returns>
        internal MediaBlock Add(MediaFrame source, MediaContainer container)
        {
            if (source == null) return null;

            lock (_syncLock)
            {
                try
                {
                    // Check if we already have a block at the given time
                    if (IsInRange(source.StartTime) && source.HasValidStartTime)
                    {
                        var repeatedBlock = _playbackBlocks.FirstOrDefault(f => f.StartTime.Ticks == source.StartTime.Ticks);
                        if (repeatedBlock != null)
                        {
                            _playbackBlocks.Remove(repeatedBlock);
                            _poolBlocks.Enqueue(repeatedBlock);
                        }
                    }

                    // if there are no available blocks, make room!
                    if (_poolBlocks.Count <= 0)
                    {
                        // Remove the first block from playback
                        var firstBlock = _playbackBlocks[0];
                        _playbackBlocks.RemoveAt(0);
                        _poolBlocks.Enqueue(firstBlock);
                    }

                    // Get a block reference from the pool and convert it!
                    var targetBlock = _poolBlocks.Dequeue();
                    var lastBlock = _playbackBlocks.Count > 0 ? _playbackBlocks[^1] : null;

                    if (!container.Convert(source, ref targetBlock, true, lastBlock))
                    {
                        // return the converted block to the pool
                        _poolBlocks.Enqueue(targetBlock);
                        return null;
                    }

                    // Add the target block to the playback blocks
                    _playbackBlocks.Add(targetBlock);

                    // return the new target block
                    return targetBlock;
                }
                finally
                {
                    // update collection-wide properties
                    UpdateCollectionProperties();
                }
            }
        }

        /// <summary>
        /// Clears all the playback blocks returning them to the
        /// block pool.
        /// </summary>
        internal void Clear()
        {
            lock (_syncLock)
            {
                // return all the blocks to the block pool
                foreach (var block in _playbackBlocks)
                    _poolBlocks.Enqueue(block);

                _playbackBlocks.Clear();
                UpdateCollectionProperties();
            }
        }

        /// <summary>
        /// Returns a formatted string with information about this buffer.
        /// </summary>
        /// <returns>The formatted string.</returns>
        internal string Debug()
        {
            lock (_syncLock)
            {
                return $"{MediaType,-12} - CAP: {Capacity,10} | FRE: {_poolBlocks.Count,7} | " +
                    $"USD: {_playbackBlocks.Count,4} |  RNG: {RangeStartTime.Format(),8} to {RangeEndTime.Format().Trim()}";
            }
        }

        /// <summary>
        /// Gets the snap, discrete position of the corresponding block.
        /// If the position is greater than the end time of the block, the
        /// start time of the next available block is returned.
        /// </summary>
        /// <param name="position">The analog position.</param>
        /// <returns>A discrete frame position.</returns>
        internal TimeSpan? GetSnapPosition(TimeSpan position)
        {
            lock (_syncLock)
            {
                if (IsMonotonic == false)
                    return this[position.Ticks]?.StartTime;

                var block = this[position.Ticks];
                if (block == null)
                    return default;

                if (block.EndTime > position)
                    return block.StartTime;

                var nextBlock = Next(block);
                return nextBlock?.StartTime ?? block.StartTime;
            }
        }

        /// <summary>
        /// Block factory method.
        /// </summary>
        /// <param name="mediaType">Type of the media.</param>
        /// <exception cref="InvalidCastException">MediaBlock does not have a valid type.</exception>
        /// <returns>An instance of the block of the specified type.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static MediaBlock CreateBlock(MediaType mediaType)
        {
            if (mediaType == MediaType.Video) return new VideoBlock();
            if (mediaType == MediaType.Audio) return new AudioBlock();
            if (mediaType == MediaType.Subtitle) return new SubtitleBlock();

            throw new InvalidCastException($"No {nameof(MediaBlock)} constructor for {nameof(MediaType)} '{mediaType}'");
        }

        /// <summary>
        /// Updates the <see cref="_playbackBlocks"/> collection properties.
        /// This method must be called whenever the collection is modified.
        /// The reason this exists is to avoid computing and iterating over these values every time they are read.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateCollectionProperties()
        {
            // Update the playback blocks sorting
            if (_playbackBlocks.Count > 0)
            {
                var maxBlockIndex = _playbackBlocks.Count - 1;

                // Perform the sorting and assignment of Previous and Next blocks
                _playbackBlocks.Sort();
                _playbackBlocks[0].Index = 0;
                _playbackBlocks[0].Previous = null;
                _playbackBlocks[0].Next = maxBlockIndex > 0 ? _playbackBlocks[1] : null;

                for (var blockIndex = 1; blockIndex <= maxBlockIndex; blockIndex++)
                {
                    _playbackBlocks[blockIndex].Index = blockIndex;
                    _playbackBlocks[blockIndex].Previous = _playbackBlocks[blockIndex - 1];
                    _playbackBlocks[blockIndex].Next = blockIndex + 1 <= maxBlockIndex ? _playbackBlocks[blockIndex + 1] : null;
                }
            }

            _lastLookupIndex = -1;
            _lastLookupTimeTicks = TimeSpan.MinValue.Ticks;

            _count = _playbackBlocks.Count;
            _rangeStartTime = _playbackBlocks.Count == 0 ? TimeSpan.Zero : _playbackBlocks[0].StartTime;
            _rangeEndTime = _playbackBlocks.Count == 0 ? TimeSpan.Zero : _playbackBlocks[^1].EndTime;
            _rangeDuration = TimeSpan.FromTicks(RangeEndTime.Ticks - RangeStartTime.Ticks);
            _rangeMidTime = TimeSpan.FromTicks(_rangeStartTime.Ticks + (_rangeDuration.Ticks / 2));
            _capacityPercent = Convert.ToDouble(_count) / Capacity;
            _isFull = _count >= Capacity;
            _rangeBitRate = _rangeDuration.TotalSeconds <= 0 || _count <= 1 ? 0 :
                Convert.ToInt64(8d * _playbackBlocks.Sum(m => m.CompressedSize) / _rangeDuration.TotalSeconds);

            // don't compute an average if we don't have blocks
            if (_count <= 0)
            {
                _averageBlockDuration = TimeSpan.Zero;
                return;
            }

            // Don't compute if we've already determined that it's non-monotonic
            if (_isNonMonotonic)
            {
                _averageBlockDuration = TimeSpan.FromTicks(
                    Convert.ToInt64(_playbackBlocks.Average(b => Convert.ToDouble(b.Duration.Ticks))));

                return;
            }

            // Monotonic verification
            var lastBlockDuration = _playbackBlocks[^1].Duration;
            _isNonMonotonic = _playbackBlocks.Any(b => b.Duration.Ticks != lastBlockDuration.Ticks);
            _isMonotonic = !_isNonMonotonic;
            _monotonicDuration = _isMonotonic ? lastBlockDuration : TimeSpan.Zero;
            _averageBlockDuration = _isMonotonic ? lastBlockDuration : TimeSpan.FromTicks(
                Convert.ToInt64(_playbackBlocks.Average(b => Convert.ToDouble(b.Duration.Ticks))));
        }

        #endregion
    }
}
