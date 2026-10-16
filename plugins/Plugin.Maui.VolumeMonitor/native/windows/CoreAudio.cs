#if WINDOWS
using System.Runtime.InteropServices;

namespace Plugin.Maui.VolumeMonitor;

internal static class CoreAudio
{
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface DeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int flow, uint mask, out nint devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int flow, int role, out Device device);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out Device device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(DeviceCallback callback);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(DeviceCallback callback);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface Device
    {
        [PreserveSig]
        int Activate(
            ref Guid iid,
            uint context,
            nint parameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object result
        );

        [PreserveSig]
        int OpenPropertyStore(uint access, out nint properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out uint state);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface EndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(VolumeCallback callback);

        [PreserveSig]
        int UnregisterControlChangeNotify(VolumeCallback callback);

        [PreserveSig]
        int GetChannelCount(out uint channels);

        [PreserveSig]
        int SetMasterVolumeLevel(float decibels, ref Guid context);

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, ref Guid context);

        [PreserveSig]
        int GetMasterVolumeLevel(out float decibels);

        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);
    }

    [
        ComVisible(true),
        Guid("657804FA-D6AD-4496-8A60-352752AF4F89"),
        InterfaceType(ComInterfaceType.InterfaceIsIUnknown)
    ]
    internal interface VolumeCallback
    {
        [PreserveSig]
        int OnNotify(nint data);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey
    {
        public Guid Format;
        public uint Id;
    }

    [
        ComVisible(true),
        Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"),
        InterfaceType(ComInterfaceType.InterfaceIsIUnknown)
    ]
    internal interface DeviceCallback
    {
        [PreserveSig]
        int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, uint state);

        [PreserveSig]
        int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);

        [PreserveSig]
        int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);

        [PreserveSig]
        int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? id);

        [PreserveSig]
        int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
    }
}
#endif
