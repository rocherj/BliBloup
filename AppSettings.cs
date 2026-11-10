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
}