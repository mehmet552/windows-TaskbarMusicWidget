using System;
using System.Runtime.InteropServices;

namespace TaskbarMusicWidget
{
    [StructLayout(LayoutKind.Sequential)]
    public struct AUDIO_VOLUME_NOTIFICATION_DATA
    {
        public Guid guidEventContext;
        public int bMuted;
        public float fMasterVolume;
        public int nChannels;
        public float afChannelVolumes;
    }

    [ComImport]
    [Guid("657804FA-D6AD-4496-8A60-352752AF4F8E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolumeCallback
    {
        void OnNotify(IntPtr pNotify);
    }

    public class AudioVolumeCallback : IAudioEndpointVolumeCallback
    {
        public event Action<float> VolumeChanged;

        public void OnNotify(IntPtr pNotify)
        {
            if (pNotify != IntPtr.Zero)
            {
                var data = Marshal.PtrToStructure<AUDIO_VOLUME_NOTIFICATION_DATA>(pNotify);
                VolumeChanged?.Invoke(data.fMasterVolume * 100f);
            }
        }
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
        int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
        int GetChannelCount(out int pnChannelCount);
        int SetMasterVolumeLevel(float fLevelDB, Guid pguidEventContext);
        int SetMasterVolumeLevelScalar(float fLevel, Guid pguidEventContext);
        int GetMasterVolumeLevel(out float pfLevelDB);
        int GetMasterVolumeLevelScalar(out float pfLevel);
        int SetChannelVolumeLevel(uint nChannel, float fLevelDB, Guid pguidEventContext);
        int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, Guid pguidEventContext);
        int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        int SetMute(bool bMute, Guid pguidEventContext);
        int GetMute(out bool pbMute);
        int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
        int VolumeStepUp(Guid pguidEventContext);
        int VolumeStepDown(Guid pguidEventContext);
        int QueryHardwareSupport(out uint pdwHardwareSupportMask);
        int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        int Activate([MarshalAs(UnmanagedType.LPStruct)] Guid iid, int dwClsCtx, IntPtr pActivationParams, out IAudioEndpointVolume ppInterface);
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr ppDevices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    public class MMDeviceEnumerator { }

    public static class AudioManager
    {
        private static IAudioEndpointVolume _epv;
        private static AudioVolumeCallback _callback;

        public static event Action<float> VolumeChanged;

        public static void Initialize()
        {
            _epv = GetMasterVolumeObject();
            if (_epv != null)
            {
                _callback = new AudioVolumeCallback();
                _callback.VolumeChanged += (v) => VolumeChanged?.Invoke(v);
                _epv.RegisterControlChangeNotify(_callback);
            }
        }

        public static void Cleanup()
        {
            if (_epv != null && _callback != null)
            {
                _epv.UnregisterControlChangeNotify(_callback);
            }
            if (_epv != null)
            {
                Marshal.ReleaseComObject(_epv);
                _epv = null;
            }
        }

        private static IAudioEndpointVolume GetMasterVolumeObject()
        {
            IMMDeviceEnumerator deviceEnumerator = null;
            try
            {
                deviceEnumerator = (IMMDeviceEnumerator)(new MMDeviceEnumerator());
                deviceEnumerator.GetDefaultAudioEndpoint(0, 1, out IMMDevice defaultDevice); // eRender = 0, eMultimedia = 1

                if (defaultDevice != null)
                {
                    Guid epvid = typeof(IAudioEndpointVolume).GUID;
                    defaultDevice.Activate(epvid, 23, IntPtr.Zero, out IAudioEndpointVolume epv); // CLSCTX_ALL = 23
                    return epv;
                }
            }
            catch
            {
                // Ignore COM errors
            }
            return null;
        }

        public static float GetMasterVolume()
        {
            try
            {
                if (_epv != null)
                {
                    _epv.GetMasterVolumeLevelScalar(out float volume);
                    return volume * 100f;
                }
                else
                {
                    var epv = GetMasterVolumeObject();
                    if (epv != null)
                    {
                        epv.GetMasterVolumeLevelScalar(out float volume);
                        Marshal.ReleaseComObject(epv);
                        return volume * 100f;
                    }
                }
            }
            catch { }
            return 50f;
        }

        public static void SetMasterVolume(float newLevel)
        {
            try
            {
                if (_epv != null)
                {
                    _epv.SetMasterVolumeLevelScalar(newLevel / 100f, Guid.Empty);
                }
                else
                {
                    var epv = GetMasterVolumeObject();
                    if (epv != null)
                    {
                        epv.SetMasterVolumeLevelScalar(newLevel / 100f, Guid.Empty);
                        Marshal.ReleaseComObject(epv);
                    }
                }
            }
            catch { }
        }
    }
}
