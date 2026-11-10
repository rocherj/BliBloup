using System;
using System.Runtime.InteropServices;
using Avalonia.Input;

namespace BoomBx.Services
{
    public enum HotkeyAvailability
    {
        /// <summary>The combination could not be probed on this platform.</summary>
        Unknown,

        /// <summary>No other application owns the combination.</summary>
        Available,

        /// <summary>Another application already registered the combination.</summary>
        UsedByAnotherApplication,

        /// <summary>Windows refuses to register the combination (OS level shortcut).</summary>
        ReservedByWindows
    }

    /// <summary>
    /// Detects hotkey combinations that are already owned by another application.
    ///
    /// BoomBx listens through a passive low-level hook, so it would still receive the keys;
    /// the other application would also react. Probing with RegisterHotKey/UnregisterHotKey
    /// reveals that conflict (error 1409) without keeping any registration.
    /// </summary>
    public static class HotkeyConflictDetector
    {
        private const int ErrorAccessDenied = 5;
        private const int ErrorHotkeyAlreadyRegistered = 1409;
        private const int ProbeHotkeyId = 0xB0B0;

        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModWin = 0x0008;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public static HotkeyAvailability Probe(IntPtr windowHandle, KeyGesture? gesture)
        {
            if (gesture is null || !OperatingSystem.IsWindows() || windowHandle == IntPtr.Zero)
            {
                return HotkeyAvailability.Unknown;
            }

            var virtualKey = VirtualKeyFromKey(gesture.Key);
            if (virtualKey == 0)
            {
                return HotkeyAvailability.Unknown;
            }

            try
            {
                if (RegisterHotKey(windowHandle, ProbeHotkeyId, Win32Modifiers(gesture.KeyModifiers), virtualKey))
                {
                    UnregisterHotKey(windowHandle, ProbeHotkeyId);
                    return HotkeyAvailability.Available;
                }

                return Marshal.GetLastWin32Error() switch
                {
                    ErrorHotkeyAlreadyRegistered => HotkeyAvailability.UsedByAnotherApplication,
                    ErrorAccessDenied => HotkeyAvailability.ReservedByWindows,
                    _ => HotkeyAvailability.Unknown
                };
            }
            catch (Exception ex)
            {
                Logger.Log($"Hotkey probe failed for {HotkeyText.Format(gesture)}: {ex.Message}");
                return HotkeyAvailability.Unknown;
            }
        }

        private static uint Win32Modifiers(KeyModifiers modifiers)
        {
            uint result = 0;
            if (modifiers.HasFlag(KeyModifiers.Alt)) result |= ModAlt;
            if (modifiers.HasFlag(KeyModifiers.Control)) result |= ModControl;
            if (modifiers.HasFlag(KeyModifiers.Shift)) result |= ModShift;
            if (modifiers.HasFlag(KeyModifiers.Meta)) result |= ModWin;
            return result;
        }

        /// <summary>
        /// Maps Avalonia's <see cref="Key"/> (WPF-compatible enum) to a Win32 virtual-key code.
        /// Returns 0 when the key has no virtual-key equivalent.
        /// </summary>
        private static uint VirtualKeyFromKey(Key key)
        {
            int code = (int)key;

            if (code >= (int)Key.A && code <= (int)Key.Z) return (uint)(0x41 + code - (int)Key.A);
            if (code >= (int)Key.D0 && code <= (int)Key.D9) return (uint)(0x30 + code - (int)Key.D0);
            if (code >= (int)Key.F1 && code <= (int)Key.F24) return (uint)(0x70 + code - (int)Key.F1);
            if (code >= (int)Key.NumPad0 && code <= (int)Key.NumPad9) return (uint)(0x60 + code - (int)Key.NumPad0);

            return key switch
            {
                Key.Space => 0x20,
                Key.Tab => 0x09,
                Key.Escape => 0x1B,
                Key.Enter => 0x0D,
                Key.Back => 0x08,
                Key.Delete => 0x2E,
                Key.Insert => 0x2D,
                Key.Home => 0x24,
                Key.End => 0x23,
                Key.PageUp => 0x21,
                Key.PageDown => 0x22,
                Key.Left => 0x25,
                Key.Up => 0x26,
                Key.Right => 0x27,
                Key.Down => 0x28,
                Key.Multiply => 0x6A,
                Key.Add => 0x6B,
                Key.Subtract => 0x6D,
                Key.Decimal => 0x6E,
                Key.Divide => 0x6F,
                Key.OemSemicolon => 0xBA,
                Key.OemPlus => 0xBB,
                Key.OemComma => 0xBC,
                Key.OemMinus => 0xBD,
                Key.OemPeriod => 0xBE,
                Key.OemQuestion => 0xBF,
                Key.OemTilde => 0xC0,
                Key.OemOpenBrackets => 0xDB,
                Key.OemPipe => 0xDC,
                Key.OemCloseBrackets => 0xDD,
                Key.OemQuotes => 0xDE,
                _ => 0
            };
        }
    }
}
