namespace Unosquare.FFME.Commands
{
    using Common;
    using System;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;

    internal partial class CommandManager
    {
        private volatile int m_PendingPriorityCommand;
        private readonly ManualResetEventSlim PriorityCommandCompleted = new(true);

        /// <summary>
        /// Gets a value indicating whether a priority command is pending.
        /// </summary>
        private bool IsPriorityCommandPending => PendingPriorityCommand != PriorityCommandType.None;

        /// <summary>
        /// Executes boilerplate code that queues priority commands.
        /// </summary>
        /// <param name="command">The command.</param>
        /// <returns>An awaitable task.</returns>
        private Task<bool> QueuePriorityCommand(PriorityCommandType command)
        {
            lock (_syncLock)
            {
                if (IsDisposed || IsDisposing || !State.IsOpen || IsDirectCommandPending || IsPriorityCommandPending)
                    return Task.FromResult(false);

                PendingPriorityCommand = command;
                PriorityCommandCompleted.Reset();

                var completed = PriorityCommandCompleted;
                var commandTask = new Task<bool>(() =>
                {
                    ResumeAsync().Wait();
                    completed.Wait();
                    return true;
                });

                commandTask.Start();
                return commandTask;
            }
        }

        /// <summary>
        /// Clears the priority commands and marks the completion event as set.
        /// </summary>
        private void ClearPriorityCommands()
        {
            lock (_syncLock)
            {
                PendingPriorityCommand = PriorityCommandType.None;
                PriorityCommandCompleted.Set();
            }
        }

        /// <summary>
        /// Provides the implementation for the Play Media Command.
        /// </summary>
        private void CommandPlayMedia()
        {
            foreach (var renderer in MediaCore.Renderers.Values)
                renderer.OnPlay();

            State.MediaState = MediaPlaybackState.Play;
        }

        /// <summary>
        /// Provides the implementation for the Pause Media Command.
        /// </summary>
        private void CommandPauseMedia()
        {
            if (State.CanPause == false)
                return;

            MediaCore.PausePlayback();

            foreach (var renderer in MediaCore.Renderers.Values)
                renderer.OnPause();

            MediaCore.ChangePlaybackPosition(SnapPositionToBlockPosition(MediaCore.PlaybackPosition));
            State.MediaState = MediaPlaybackState.Pause;
        }

        /// <summary>
        /// Provides the implementation for the Stop Media Command.
        /// </summary>
        private void CommandStopMedia()
        {
            if (State.IsSeekable == false)
                return;

            MediaCore.ResetPlaybackPosition();

            SeekMedia(new SeekOperation(TimeSpan.MinValue, SeekMode.Stop), CancellationToken.None);

            foreach (var renderer in MediaCore.Renderers.Values)
                renderer.OnStop();

            State.MediaState = MediaPlaybackState.Stop;
        }

        /// <summary>
        /// Returns the value of a discrete frame position of the main media component if possible.
        /// Otherwise, it simply rounds the position to the nearest millisecond.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <returns>The snapped, discrete, normalized position.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private TimeSpan SnapPositionToBlockPosition(TimeSpan position)
        {
            if (MediaCore.Container == null)
                return position.Normalize();

            var t = MediaCore.Container?.Components?.SeekableMediaType ?? MediaType.None;
            var blocks = MediaCore.Blocks[t];
            if (blocks == null) return position.Normalize();

            return blocks.GetSnapPosition(position) ?? position.Normalize();
        }
    }
}
