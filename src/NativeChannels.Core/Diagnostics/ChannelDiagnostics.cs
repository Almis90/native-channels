using System.Diagnostics;

namespace NativeChannels;

/// <summary>Opt-in structured diagnostics shared by a service and its channel handles.</summary>
/// <remarks>
/// The observer runs synchronously on the calling or handler thread and may run concurrently.
/// Keep it short, thread-safe, and nonblocking. Observer exceptions are ignored.
/// Names, error codes, and platform labels are application-defined; do not put secrets in them.
/// </remarks>
public sealed class ChannelDiagnostics
{
    private readonly Action<ChannelDiagnosticEvent> _observer;

    /// <summary>Creates a diagnostic sink. No notifications are emitted until assigned to a service.</summary>
    public ChannelDiagnostics(Action<ChannelDiagnosticEvent> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _observer = observer;
    }

    internal Operation Start(
        ChannelDiagnosticKind kind,
        ChannelDiagnosticDirection direction,
        string channel,
        string? method = null,
        TimeSpan? timeout = null
    ) => new(this, kind, direction, channel, method, timeout);

    private void Report(ChannelDiagnosticEvent notification)
    {
        try
        {
            _observer(notification);
        }
        catch (Exception)
        {
            // Diagnostics must never replace a result or interfere with cancellation and cleanup.
        }
    }

    internal sealed class Operation : IDisposable
    {
        private readonly ChannelDiagnostics _owner;
        private readonly ChannelDiagnosticEvent _started;
        private readonly long _timestamp = Stopwatch.GetTimestamp();
        private int _completed;

        internal string? Method { get; set; }

        internal Operation(
            ChannelDiagnostics owner,
            ChannelDiagnosticKind kind,
            ChannelDiagnosticDirection direction,
            string channel,
            string? method,
            TimeSpan? timeout
        )
        {
            _owner = owner;
            Method = method;
            _started = new(
                Guid.NewGuid(),
                kind,
                direction,
                ChannelDiagnosticStage.Started,
                channel,
                method,
                DateTimeOffset.UtcNow,
                TimeSpan.Zero,
                timeout,
                null,
                null,
                null
            );
            owner.Report(_started);
        }

        internal CancellationTokenRegistration ObserveCancellation(CancellationToken cancellationToken) =>
            cancellationToken.Register(
                static state => ((Operation)state!).Fail(new OperationCanceledException()),
                this
            );

        internal void Fail(Exception error) => Complete(error);

        private void Complete(Exception? error)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
                return;

            _owner.Report(
                _started with
                {
                    Stage = ChannelDiagnosticStage.Completed,
                    Method = Method,
                    Timestamp = DateTimeOffset.UtcNow,
                    Elapsed = Stopwatch.GetElapsedTime(_timestamp),
                    Outcome = error switch
                    {
                        null => ChannelDiagnosticOutcome.Success,
                        OperationCanceledException => ChannelDiagnosticOutcome.Cancelled,
                        TimeoutException => ChannelDiagnosticOutcome.TimedOut,
                        ObjectDisposedException => ChannelDiagnosticOutcome.Disposed,
                        ChannelNotFoundException => ChannelDiagnosticOutcome.ChannelNotFound,
                        MethodNotImplementedException => ChannelDiagnosticOutcome.NotImplemented,
                        ChannelSerializationException => ChannelDiagnosticOutcome.SerializationError,
                        ChannelTransportException => ChannelDiagnosticOutcome.TransportError,
                        ChannelException => ChannelDiagnosticOutcome.ChannelError,
                        _ => ChannelDiagnosticOutcome.Failed,
                    },
                    ErrorCode = (error as ChannelException)?.Code,
                    Platform = (error as ChannelException)?.Platform,
                }
            );
        }

        public void Dispose() => Complete(null);
    }
}
