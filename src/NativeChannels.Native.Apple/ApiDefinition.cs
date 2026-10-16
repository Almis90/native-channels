using Foundation;
using ObjCRuntime;

namespace NativeChannels.Native.Apple;

interface IReply { }

interface IMessageHandler { }

interface IRequestContext { }

interface IRequestHandler { }

interface ICancellationHandler { }

[Protocol, Model]
[BaseType(typeof(NSObject), Name = "MCCancellationHandler")]
interface CancellationHandler
{
    [Abstract, Export("onCancel")]
    void OnCancel();
}

[Protocol, Model]
[BaseType(typeof(NSObject), Name = "MCRequestContext")]
interface RequestContext : Reply
{
    [Abstract, Export("requestId")]
    string RequestId { get; }

    [Abstract, Export("isCancelled")]
    bool IsCancelled { get; }

    [Abstract, Export("onCancelled:")]
    void OnCancelled(ICancellationHandler handler);
}

[Protocol, Model]
[BaseType(typeof(NSObject), Name = "MCRequestHandler")]
interface RequestHandler
{
    [Abstract, Export("receiveRequest:payload:context:")]
    void ReceiveRequest(string channel, NSData payload, IRequestContext context);
}

[Protocol, Model]
[BaseType(typeof(NSObject), Name = "MCReply")]
interface Reply
{
    [Abstract, Export("complete:payload:")]
    void Complete(int status, NSData payload);
}

[Protocol, Model]
[BaseType(typeof(NSObject), Name = "MCMessageHandler")]
interface MessageHandler
{
    [Abstract, Export("receive:payload:reply:")]
    void Receive(string channel, NSData payload, IReply reply);
}

[BaseType(typeof(NSObject), Name = "MCNativeBridge")]
interface NativeBridge
{
    [Export("protocolVersion")]
    int ProtocolVersion { get; }

    [Export("setRequestReceiver:")]
    void SetRequestReceiver([NullAllowed] IRequestHandler handler);

    [Export("sendRequest:channel:payload:reply:")]
    void SendRequest(string id, string channel, NSData payload, IReply reply);

    [Export("cancel:")]
    void Cancel(string id);

    [Export("closeAsync:")]
    void CloseAsync(IReply completion);

    [Export("setReceiver:")]
    void SetReceiver([NullAllowed] IMessageHandler handler);

    [Export("loadModule:")]
    [return: NullAllowed]
    string LoadModule(string className);

    [Export("send:payload:reply:")]
    void Send(string channel, NSData payload, IReply reply);

    [Export("invokeManaged:payload:reply:")]
    void InvokeManaged(string channel, NSData payload, IReply reply);

    [Export("close")]
    void Close();
}

[BaseType(typeof(NSObject), Name = "MCNativeViewHost")]
[DisableDefaultCtor]
interface NativeViewHost
{
    [Static, Export("create:bridge:instanceId:channelName:arguments:error:")]
    [return: NullAllowed]
    NativeViewHost Create(
        string className,
        NativeBridge bridge,
        string instanceId,
        string channelName,
        NSData arguments,
        [NullAllowed] out NSError error
    );

    [NullAllowed, Export("view")]
    UIKit.UIView View { get; }

    [Export("close")]
    void Close();
}
