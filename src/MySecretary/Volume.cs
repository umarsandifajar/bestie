using System.Runtime.InteropServices;
namespace MySecretary;

// Core Audio: volume + mute sebuah endpoint (urutan metode HARUS sama dengan vtable COM aslinya).
[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolume
{
    void RegisterControlChangeNotify(IntPtr notify);
    void UnregisterControlChangeNotify(IntPtr notify);
    void GetChannelCount(out uint count);
    void SetMasterVolumeLevel(float level, ref Guid ctx);
    void SetMasterVolumeLevelScalar(float level, ref Guid ctx);
    void GetMasterVolumeLevel(out float level);
    void GetMasterVolumeLevelScalar(out float level);
    void SetChannelVolumeLevel(uint channel, float level, ref Guid ctx);
    void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid ctx);
    void GetChannelVolumeLevel(uint channel, out float level);
    void GetChannelVolumeLevelScalar(uint channel, out float level);
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    void GetVolumeStepInfo(out uint step, out uint stepCount);
    void VolumeStepUp(ref Guid ctx);
    void VolumeStepDown(ref Guid ctx);
    void QueryHardwareSupport(out uint mask);
    void GetVolumeRange(out float min, out float max, out float inc);
}

// Volume speaker (output default) + mute speaker + mute mikrofon (input default). Dipakai side notch.
sealed class VolumeCtl
{
    IAudioEndpointVolume? spk, mic;

    public float Volume { get; private set; }
    public bool SpkMuted { get; private set; }
    public bool MicMuted { get; private set; }
    public bool HasSpk => spk != null;
    public bool HasMic => mic != null;

    static IAudioEndpointVolume? Open(int flow)   // flow: 0 = output (render), 1 = input (capture)
    {
        try
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            en.GetDefaultAudioEndpoint(flow, 0, out var dev);          // eConsole
            var iid = typeof(IAudioEndpointVolume).GUID;
            dev.Activate(ref iid, 23, IntPtr.Zero, out var o);          // CLSCTX_ALL
            Marshal.ReleaseComObject(dev); Marshal.ReleaseComObject(en);
            return (IAudioEndpointVolume)o;
        }
        catch { return null; }
    }

    static void Free(ref IAudioEndpointVolume? e)
    {
        if (e != null) { try { Marshal.ReleaseComObject(e); } catch { } e = null; }
    }

    // Ambil ulang perangkat default (dipanggil tiap notch dibuka, jaga-jaga headset/mic berganti).
    public void Acquire()
    {
        Free(ref spk); Free(ref mic);
        spk = Open(0); mic = Open(1);
        Poll();
    }

    // Baca status terbaru (volume/mute bisa diubah dari luar, misal tombol keyboard). Return true bila ada yang berubah.
    public bool Poll()
    {
        float v = Volume; bool sm = SpkMuted, mm = MicMuted;
        if (spk != null)
        {
            try { spk.GetMasterVolumeLevelScalar(out v); spk.GetMute(out sm); }
            catch { Free(ref spk); }
        }
        if (mic != null)
        {
            try { mic.GetMute(out mm); }
            catch { Free(ref mic); }
        }
        bool changed = Math.Abs(v - Volume) > .001f || sm != SpkMuted || mm != MicMuted;
        Volume = v; SpkMuted = sm; MicMuted = mm;
        return changed;
    }

    public void SetVolume(float v)
    {
        if (spk == null) return;
        v = Math.Clamp(v, 0f, 1f);
        try
        {
            var g = Guid.Empty;
            spk.SetMasterVolumeLevelScalar(v, ref g);
            if (v > 0 && SpkMuted) { spk.SetMute(false, ref g); SpkMuted = false; }   // menggeser volume = otomatis unmute, seperti Windows
            Volume = v;
        }
        catch { }
    }

    public void ToggleSpk()
    {
        if (spk == null) return;
        try { var g = Guid.Empty; bool m = !SpkMuted; spk.SetMute(m, ref g); SpkMuted = m; } catch { }
    }

    public void ToggleMic()
    {
        if (mic == null) return;
        try { var g = Guid.Empty; bool m = !MicMuted; mic.SetMute(m, ref g); MicMuted = m; } catch { }
    }
}
