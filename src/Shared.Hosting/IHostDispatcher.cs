namespace NativeChannels;

internal interface IHostDispatcher
{
    bool HasThreadAccess { get; }
    void VerifyAccess();
    Task InvokeAsync(Func<Task> action);
}
