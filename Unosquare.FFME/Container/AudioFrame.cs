using FFmpeg.AutoGen;
using System.Threading;

namespace Unosquare.FFME.Container;

using Common;
using System;

/// <inheritdoc />
/// <summary>
/// Represents a wrapper from an unmanaged FFmpeg audio frame.
/// </summary>
/// <seealso cref="MediaFrame" />
/// <seealso cref="IDisposable" />
internal sealed unsafe class AudioFrame : MediaFrame
{
    #region Private Members

    private readonly Lock _disposeLock = new();
    private bool _isDisposed;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioFrame" /> class.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <param name="component">The component.</param>
    internal AudioFrame(AVFrame* frame, MediaComponent component)
        : base(frame, component, MediaType.Audio)
    {
        // Compute the start time.
        frame->pts = frame->best_effort_timestamp;
        HasValidStartTime = frame->pts != ffmpeg.AV_NOPTS_VALUE;
        StartTime = frame->pts == ffmpeg.AV_NOPTS_VALUE ?
            TimeSpan.FromTicks(0) :
            TimeSpan.FromTicks(frame->pts.ToTimeSpan(StreamTimeBase).Ticks);

        // Compute the audio frame duration
        Duration = frame->duration > 0 ?
            frame->duration.ToTimeSpan(StreamTimeBase) :
            TimeSpan.FromTicks(Convert.ToInt64(TimeSpan.TicksPerMillisecond * 1000d * frame->nb_samples / frame->sample_rate));

        // Compute the audio frame end time
        EndTime = TimeSpan.FromTicks(StartTime.Ticks + Duration.Ticks);
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the pointer to the unmanaged frame.
    /// </summary>
    internal AVFrame* Pointer => (AVFrame*)InternalPointer;

    #endregion

    #region IDisposable Support

    /// <inheritdoc />
    public override void Dispose()
    {
        lock (_disposeLock)
        {
            if (_isDisposed)
                return;

            if (InternalPointer != IntPtr.Zero)
                ReleaseAVFrame(Pointer);

            InternalPointer = IntPtr.Zero;
            _isDisposed = true;
        }
    }

    #endregion
}
