using System.Reflection;

public class AppSettings
{
    public string? LastPlaybackDevice { get; set; }
    public string? LastCaptureDevice { get; set; }

    public string PlayPauseHotkey { get; set; } = "F9";
    public string StopHotkey { get; set; } = "F10";
    public string PauseHotkey { get; set; } = "F11";

    /// <summary>Emergency stop combination; pressing it immediately silences playback.</summary>
    public string PanicHotkey { get; set; } = "Esc";

    /// <summary>When false the global hook stays installed but every combination is ignored.</summary>
    public bool HotkeysEnabled { get; set; } = true;

    /// <summary>
    /// WASAPI capture buffer in milliseconds. Lower values reduce latency but ask more of the
    /// system; higher values are more tolerant of slow machines and busy audio devices.
    /// </summary>
    public int CaptureBufferMilliseconds { get; set; } = 40;

    /// <summary>
    /// Capture in WASAPI exclusive mode: the lowest latency, but the microphone is then blocked
    /// for every other application while BliBloup runs.
    /// </summary>
    public bool CaptureExclusiveMode { get; set; }

    /// <summary>Ask Windows MMCSS for the "Pro Audio" thread priority on the capture thread.</summary>
    public bool CaptureProAudioPriority { get; set; } = true;

    /// <summary>Real-time microphone voice changer settings.</summary>
    public VoiceChangerSettings VoiceChanger { get; set; } = new();
}