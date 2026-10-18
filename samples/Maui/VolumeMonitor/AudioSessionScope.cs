namespace VolumeMonitor.Sample;

/// <summary>The foreground demo owns its audio session independently of volume subscriptions.</summary>
internal sealed class AudioSessionScope : IDisposable
{
    public AudioSessionScope()
    {
#if IOS
        var session = AVFoundation.AVAudioSession.SharedInstance();
        var error = session.SetCategory(AVFoundation.AVAudioSessionCategory.Ambient);
        if (error is not null)
            throw new InvalidOperationException(error.LocalizedDescription);
        if (!session.SetActive(true, out error))
            throw new InvalidOperationException(error?.LocalizedDescription ?? "Cannot activate the audio session.");
#endif
    }

    public void Dispose()
    {
#if IOS
        if (
            !AVFoundation
                .AVAudioSession.SharedInstance()
                .SetActive(false, AVFoundation.AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation, out var error)
        )
            throw new InvalidOperationException(error?.LocalizedDescription ?? "Cannot deactivate the audio session.");
#endif
    }
}
