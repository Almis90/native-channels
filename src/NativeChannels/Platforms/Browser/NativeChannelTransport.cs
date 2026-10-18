#if BROWSER
using System.Collections.Concurrent;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

namespace NativeChannels;

public sealed partial class NativeChannelTransport
{
    private const string BrowserModuleName = "NativeChannels.Transport";
    private static Task<JSObject>? s_browserModule;
    private readonly ConcurrentDictionary<Guid, BrowserRequest> _browserRequests = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _browserIncoming = new();
    private Task _browserReady = null!;
    private JSObject? _browserSession;

    private partial void Initialize(IEnumerable<NativeModuleRegistration> modules)
    {
        if (!OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException("The browser transport requires WebAssembly in a browser.");
        var urls = modules
            .Select(module =>
                module.BrowserModule
                ?? throw new PlatformNotSupportedException("This module has no packaged BrowserModule URL.")
            )
            .ToArray();
        if (urls.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Browser module URLs must not be empty.", nameof(modules));
        _browserReady = InitializeBrowserAsync(urls);
        // Observe failures even when the session is disposed without making a request.
        _ = _browserReady.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task InitializeBrowserAsync(string[] urls)
    {
        using var document = JSHost.GlobalThis.GetPropertyAsJSObject("document")!;
        var baseUri = new Uri(document.GetPropertyAsString("baseURI")!);
        await (
            s_browserModule ??= JSHost.ImportAsync(
                BrowserModuleName,
                new Uri(baseUri, "./_content/NativeChannels/nativechannels.mjs").AbsoluteUri
            )
        );
        if (_disposed)
            return;
        _browserSession = BrowserInterop.Create(ReceiveBrowserMessage);
        try
        {
            await BrowserInterop.LoadModules(_browserSession, urls);
        }
        catch (Exception error)
        {
            await BrowserInterop.Close(_browserSession);
            throw new ChannelTransportException(
                "Browser module initialization failed. Check the packaged module URLs and register exports.",
                error
            );
        }
    }

    private partial IDisposable SendNative(TransportRequest request, Action<TransportResponse> completed)
    {
        var operation = new BrowserRequest(completed);
        if (!_browserRequests.TryAdd(request.Id, operation))
            throw new ChannelTransportException("Duplicate browser request ID.");
        _ = SendBrowserAsync(request with { Payload = request.Payload.ToArray() }, operation);
        return operation;
    }

    private async Task SendBrowserAsync(TransportRequest request, BrowserRequest operation)
    {
        try
        {
            await _browserReady;
            if (
                !_disposed
                && _browserRequests.TryGetValue(request.Id, out var current)
                && ReferenceEquals(current, operation)
            )
                BrowserInterop.Send(
                    _browserSession!,
                    request.Id.ToString(),
                    request.Channel,
                    Convert.ToBase64String(request.Payload.Span)
                );
        }
        catch (Exception error)
        {
            if (
                _browserRequests.TryRemove(new KeyValuePair<Guid, BrowserRequest>(request.Id, operation))
                && _pending.TryGetValue(request.Id, out var pending)
            )
                pending.Completion.TrySetException(
                    new ChannelTransportException("Browser channel dispatch failed.", error)
                );
        }
    }

    private void ReceiveBrowserMessage(string message)
    {
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        var id = root.GetProperty("id").GetGuid();
        switch (root.GetProperty("kind").GetString())
        {
            case "reply":
                if (_browserRequests.TryRemove(id, out var completed))
                {
                    var status = (TransportStatus)root.GetProperty("status").GetInt32();
                    completed.Complete(new(status, Convert.FromBase64String(root.GetProperty("payload").GetString()!)));
                }
                break;
            case "request":
                if (!_disposed)
                    _ = ReceiveBrowserAsync(
                        new(
                            id,
                            root.GetProperty("channel").GetString()!,
                            Convert.FromBase64String(root.GetProperty("payload").GetString()!)
                        )
                    );
                break;
            case "cancel":
                if (_browserIncoming.TryGetValue(id, out var cancellation))
                    CancelBrowserRequest(cancellation);
                break;
        }
    }

    private async Task ReceiveBrowserAsync(TransportRequest request)
    {
        using var cancellation = new CancellationTokenSource();
        if (!_browserIncoming.TryAdd(request.Id, cancellation))
            return;
        TransportResponse response;
        try
        {
            response = await ReceiveAsync(request, cancellation.Token).AsTask().WaitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            response = new(TransportStatus.Cancelled, default);
        }
        catch (Exception)
        {
            response = new(TransportStatus.HandlerError, default);
        }
        finally
        {
            _browserIncoming.TryRemove(request.Id, out _);
        }
        if (!_disposed)
            BrowserInterop.Reply(
                _browserSession!,
                request.Id.ToString(),
                (int)response.Status,
                Convert.ToBase64String(response.Payload.Span)
            );
    }

    private partial void CancelNative(Guid id)
    {
        if (_browserRequests.TryRemove(id, out var completed))
        {
            if (_browserSession is not null)
                BrowserInterop.Cancel(_browserSession, id.ToString());
            completed.Complete(new(TransportStatus.Cancelled, default));
        }
    }

    private async partial Task ShutdownNativeAsync()
    {
        foreach (var id in _browserRequests.Keys)
            if (_browserRequests.TryRemove(id, out var completed))
                completed.Complete(new(TransportStatus.Disposed, default));
        foreach (var cancellation in _browserIncoming.Values)
            CancelBrowserRequest(cancellation);
        // Close immediately, including while an adapter is loading. A late register result
        // is disposed by the JS loader; shutdown does not wait on an uncooperative adapter.
        if (_browserSession is not null)
        {
            try
            {
                await BrowserInterop.Close(_browserSession);
            }
            finally
            {
                _browserSession.Dispose();
            }
        }
    }

    private static void CancelBrowserRequest(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (AggregateException error)
        {
            System.Diagnostics.Trace.TraceError("Browser cancellation callback failed: {0}", error);
        }
    }

    private sealed class BrowserRequest(Action<TransportResponse> completed) : IDisposable
    {
        internal void Complete(TransportResponse response) => completed(response);

        public void Dispose() { }
    }

    private static partial class BrowserInterop
    {
        [JSImport("createSession", BrowserModuleName)]
        internal static partial JSObject Create([JSMarshalAs<JSType.Function<JSType.String>>] Action<string> receive);

        [JSImport("loadModules", BrowserModuleName)]
        internal static partial Task LoadModules(JSObject session, string[] urls);

        [JSImport("send", BrowserModuleName)]
        internal static partial void Send(JSObject session, string id, string channel, string payload);

        [JSImport("cancel", BrowserModuleName)]
        internal static partial void Cancel(JSObject session, string id);

        [JSImport("reply", BrowserModuleName)]
        internal static partial void Reply(JSObject session, string id, int status, string payload);

        [JSImport("dispose", BrowserModuleName)]
        internal static partial Task Close(JSObject session);
    }
}
#endif
