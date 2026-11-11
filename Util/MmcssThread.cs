using System;
using System.Runtime.InteropServices;

/// <summary>
/// Registers the calling thread with the Windows Multimedia Class Scheduler Service under the
/// "Pro Audio" task. The registration is released automatically when the capture thread ends.
/// </summary>
internal static class MmcssThread
{
    [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AvSetMmThreadCharacteristics(string taskName, out int taskIndex);

    public static void TryEnableProAudio()
    {
        try
        {
            var handle = AvSetMmThreadCharacteristics("Pro Audio", out int taskIndex);

            if (handle == IntPtr.Zero)
            {
                Logger.Log($"MMCSS registration failed (Win32 error {Marshal.GetLastWin32Error()}).");
                return;
            }

            Logger.Log($"MMCSS Pro Audio thread priority enabled (task index {taskIndex}).");
        }
        catch (Exception ex)
        {
            Logger.Log($"MMCSS registration failed: {ex.Message}");
        }
    }
}
