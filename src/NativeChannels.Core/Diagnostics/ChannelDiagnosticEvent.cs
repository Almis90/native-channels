namespace NativeChannels;

/// <summary>The managed channel contract being observed.</summary>
public enum ChannelDiagnosticKind
{
    Method,
    Message,
}

/// <summary>Direction relative to the managed channel service reporting the operation.</summary>
public enum ChannelDiagnosticDirection
{
    Outgoing,
    Incoming,
}

/// <summary>Each observed operation emits a start and at most one completion.</summary>
public enum ChannelDiagnosticStage
{
    Started,
    Completed,
}

/// <summary>The terminal result of an observed operation.</summary>
public enum ChannelDiagnosticOutcome
{
    Success,
    Cancelled,
    TimedOut,
    Disposed,
    ChannelNotFound,
    NotImplemented,
    SerializationError,
    TransportError,
    ChannelError,
    Failed,
}

/// <summary>Metadata only: never contains arguments, results, bytes, exception messages, or error details.</summary>
/// <param name="OperationId">Local diagnostic ID shared by the start and completion; not a wire correlation ID.</param>
/// <param name="Kind">Method or basic message operation.</param>
/// <param name="Direction">Direction relative to this managed service.</param>
/// <param name="Stage">Start or completion.</param>
/// <param name="Channel">Application-defined endpoint name.</param>
/// <param name="Method">Method name, if known. Incoming starts precede decoding and have no method name.</param>
/// <param name="Timestamp">UTC time of this notification.</param>
/// <param name="Elapsed">Monotonic elapsed time; zero at the start.</param>
/// <param name="Timeout">Effective outgoing timeout, including infinite; null for incoming handlers.</param>
/// <param name="Outcome">Terminal outcome; null at the start.</param>
/// <param name="ErrorCode">Structured channel error code, if available.</param>
/// <param name="Platform">Structured error platform, if available; not an inferred execution platform.</param>
public sealed record ChannelDiagnosticEvent(
    Guid OperationId,
    ChannelDiagnosticKind Kind,
    ChannelDiagnosticDirection Direction,
    ChannelDiagnosticStage Stage,
    string Channel,
    string? Method,
    DateTimeOffset Timestamp,
    TimeSpan Elapsed,
    TimeSpan? Timeout,
    ChannelDiagnosticOutcome? Outcome,
    string? ErrorCode,
    string? Platform
);
