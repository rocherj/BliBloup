using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;

namespace BoomBx.Services
{
    /// <summary>
    /// Converts keyboard combinations between <see cref="KeyGesture"/>, user facing text and
    /// the persisted settings format, and explains why a combination cannot be used.
    ///
    /// The persisted format is canonical ("Ctrl+Alt+1", "F9", "Escape"): it round-trips with
    /// <see cref="TryParse"/>, and legacy WPF-style names ("D1", "OemSemicolon") are accepted.
    /// </summary>
    public static class HotkeyText
    {
        private static readonly HashSet<string> ReservedCombinations = BuildReservedCombinations();

        /// <summary>Formats a gesture as the canonical text stored in settings and shown in the UI.</summary>
        public static string Format(KeyGesture gesture)
        {
            if (gesture is null)
            {
                return string.Empty;
            }

            var parts = new List<string>(5);

            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Win");

            parts.Add(KeyToken(gesture.Key));
            return string.Join("+", parts);
        }

        /// <summary>
        /// Parses settings text into a gesture. Empty text means "no hotkey" and returns false
        /// with a null error so callers can treat it as unassigned rather than invalid.
        /// </summary>
        public static bool TryParse(string? text, out KeyGesture? gesture, out string? error)
        {
            gesture = null;
            error = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                error = "no hotkey assigned";
                return false;
            }

            var tokens = text
                .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (tokens.Length == 0)
            {
                error = "empty hotkey";
                return false;
            }

            var modifiers = KeyModifiers.None;

            for (int i = 0; i < tokens.Length - 1; i++)
            {
                var modifier = tokens[i].ToLowerInvariant();
                switch (modifier)
                {
                    case "ctrl":
                    case "control":
                        modifiers |= KeyModifiers.Control;
                        break;
                    case "alt":
                        modifiers |= KeyModifiers.Alt;
                        break;
                    case "shift":
                        modifiers |= KeyModifiers.Shift;
                        break;
                    case "win":
                    case "win/":
                    case "windows":
                    case "meta":
                    case "super":
                    case "cmd":
                        modifiers |= KeyModifiers.Meta;
                        break;
                    default:
                        error = $"unknown modifier '{tokens[i]}'";
                        return false;
                }
            }

            if (!TryParseKeyToken(tokens[^1], out var key))
            {
                error = $"unknown key '{tokens[^1]}'";
                return false;
            }

            gesture = new KeyGesture(key, modifiers);
            return true;
        }

        /// <summary>
        /// Validates a combination before it is saved. Mirrors the guards applied while capturing
        /// a key so imported settings are checked the same way.
        /// </summary>
        public static bool IsAssignable(KeyGesture? gesture, out string reason)
        {
            reason = string.Empty;

            if (gesture is null)
            {
                reason = "no key combination";
                return false;
            }

            if (IsModifierKey(gesture.Key))
            {
                reason = "A modifier key alone cannot be used as a hotkey.";
                return false;
            }

            if (gesture.Key == Key.None)
            {
                reason = "This key cannot be used as a hotkey.";
                return false;
            }

            var text = Format(gesture);

            if (ReservedCombinations.Contains(text))
            {
                reason = $"\"{text}\" is reserved by Windows and cannot be captured reliably.";
                return false;
            }

            if (gesture.KeyModifiers == KeyModifiers.None && IsPrintableKey(gesture.Key))
            {
                reason = "Add Ctrl, Alt, Shift or Win: a plain letter or digit would also be typed into the foreground application.";
                return false;
            }

            return true;
        }

        public static bool IsModifierKey(Key key) => key is
            Key.LeftCtrl or Key.RightCtrl or
            Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or
            Key.LWin or Key.RWin;

        /// <summary>Keys that produce a character and would leak into the focused application.</summary>
        private static bool IsPrintableKey(Key key)
        {
            int code = (int)key;

            if (code >= (int)Key.A && code <= (int)Key.Z) return true;
            if (code >= (int)Key.D0 && code <= (int)Key.D9) return true;

            return key is Key.Space or
                Key.OemSemicolon or Key.OemPlus or Key.OemComma or Key.OemMinus or
                Key.OemPeriod or Key.OemQuestion or Key.OemTilde or Key.OemOpenBrackets or
                Key.OemPipe or Key.OemCloseBrackets or Key.OemQuotes;
        }

        /// <summary>Human readable single key name used by the canonical format.</summary>
        public static string KeyToken(Key key)
        {
            int code = (int)key;

            if (code >= (int)Key.A && code <= (int)Key.Z)
            {
                return ((char)('A' + code - (int)Key.A)).ToString();
            }

            if (code >= (int)Key.D0 && code <= (int)Key.D9)
            {
                return ((char)('0' + code - (int)Key.D0)).ToString();
            }

            if (code >= (int)Key.F1 && code <= (int)Key.F24)
            {
                return $"F{1 + code - (int)Key.F1}";
            }

            if (code >= (int)Key.NumPad0 && code <= (int)Key.NumPad9)
            {
                return $"Num{code - (int)Key.NumPad0}";
            }

            return key switch
            {
                Key.Space => "Space",
                Key.Tab => "Tab",
                Key.Escape => "Esc",
                Key.Enter => "Enter",
                Key.Back => "Backspace",
                Key.Delete => "Delete",
                Key.Insert => "Insert",
                Key.Home => "Home",
                Key.End => "End",
                Key.PageUp => "PageUp",
                Key.PageDown => "PageDown",
                Key.Up => "Up",
                Key.Down => "Down",
                Key.Left => "Left",
                Key.Right => "Right",
                Key.Multiply => "NumMultiply",
                Key.Add => "NumAdd",
                Key.Subtract => "NumSubtract",
                Key.Decimal => "NumDecimal",
                Key.Divide => "NumDivide",
                Key.OemSemicolon => ";",
                Key.OemPlus => "=",
                Key.OemComma => ",",
                Key.OemMinus => "-",
                Key.OemPeriod => ".",
                Key.OemQuestion => "/",
                Key.OemTilde => "`",
                Key.OemOpenBrackets => "[",
                Key.OemPipe => "\\",
                Key.OemCloseBrackets => "]",
                Key.OemQuotes => "'",
                _ => key.ToString()
            };
        }

        private static bool TryParseKeyToken(string token, out Key key)
        {
            key = Key.None;
            var value = token.Trim();

            if (value.Length == 0)
            {
                return false;
            }

            if (value.Length == 1)
            {
                var c = char.ToUpperInvariant(value[0]);

                if (c >= 'A' && c <= 'Z')
                {
                    key = (Key)((int)Key.A + (c - 'A'));
                    return true;
                }

                if (c >= '0' && c <= '9')
                {
                    key = (Key)((int)Key.D0 + (c - '0'));
                    return true;
                }
            }

            if (value.Length >= 2)
            {
                if ((value[0] == 'F' || value[0] == 'f') && value.Length <= 3 &&
                    int.TryParse(value.AsSpan(1), out var functionKey) &&
                    functionKey >= 1 && functionKey <= 24)
                {
                    key = (Key)((int)Key.F1 + (functionKey - 1));
                    return true;
                }

                if (value.StartsWith("Num", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(value.AsSpan(3), out var numPadKey) &&
                    numPadKey >= 0 && numPadKey <= 9)
                {
                    key = (Key)((int)Key.NumPad0 + numPadKey);
                    return true;
                }
            }

            switch (value.ToLowerInvariant())
            {
                case "space": key = Key.Space; return true;
                case "tab": key = Key.Tab; return true;
                case "esc":
                case "escape": key = Key.Escape; return true;
                case "enter":
                case "return": key = Key.Enter; return true;
                case "backspace":
                case "back": key = Key.Back; return true;
                case "delete":
                case "del": key = Key.Delete; return true;
                case "insert":
                case "ins": key = Key.Insert; return true;
                case "home": key = Key.Home; return true;
                case "end": key = Key.End; return true;
                case "pageup":
                case "pgup": key = Key.PageUp; return true;
                case "pagedown":
                case "pgdn": key = Key.PageDown; return true;
                case "up": key = Key.Up; return true;
                case "down": key = Key.Down; return true;
                case "left": key = Key.Left; return true;
                case "right": key = Key.Right; return true;
                case "numadd":
                case "add": key = Key.Add; return true;
                case "numsubtract":
                case "subtract": key = Key.Subtract; return true;
                case "nummultiply":
                case "multiply": key = Key.Multiply; return true;
                case "numdivide":
                case "divide": key = Key.Divide; return true;
                case "numdecimal":
                case "decimal": key = Key.Decimal; return true;
                case ";": key = Key.OemSemicolon; return true;
                case "=": key = Key.OemPlus; return true;
                case ",": key = Key.OemComma; return true;
                case "-": key = Key.OemMinus; return true;
                case ".": key = Key.OemPeriod; return true;
                case "/": key = Key.OemQuestion; return true;
                case "`": key = Key.OemTilde; return true;
                case "[": key = Key.OemOpenBrackets; return true;
                case "\\": key = Key.OemPipe; return true;
                case "]": key = Key.OemCloseBrackets; return true;
                case "'": key = Key.OemQuotes; return true;
            }

            // Accepts the raw enum names still found in older settings ("D1", "OemSemicolon"...).
            return Enum.TryParse(value, ignoreCase: true, out key) && key != Key.None;
        }

        private static HashSet<string> BuildReservedCombinations()
        {
            var reserved = new[]
            {
                new KeyGesture(Key.Tab, KeyModifiers.Alt),
                new KeyGesture(Key.Tab, KeyModifiers.Control | KeyModifiers.Alt),
                new KeyGesture(Key.F4, KeyModifiers.Alt),
                new KeyGesture(Key.Escape, KeyModifiers.Alt),
                new KeyGesture(Key.Escape, KeyModifiers.Control),
                new KeyGesture(Key.Escape, KeyModifiers.Control | KeyModifiers.Shift),
                new KeyGesture(Key.Delete, KeyModifiers.Control | KeyModifiers.Alt),
                new KeyGesture(Key.Tab, KeyModifiers.Meta),
                new KeyGesture(Key.Space, KeyModifiers.Meta),
                new KeyGesture(Key.L, KeyModifiers.Meta),
                new KeyGesture(Key.D, KeyModifiers.Meta),
                new KeyGesture(Key.E, KeyModifiers.Meta),
                new KeyGesture(Key.R, KeyModifiers.Meta),
                new KeyGesture(Key.S, KeyModifiers.Meta),
                new KeyGesture(Key.S, KeyModifiers.Meta | KeyModifiers.Shift),
                new KeyGesture(Key.D, KeyModifiers.Meta | KeyModifiers.Control),
                new KeyGesture(Key.F4, KeyModifiers.Meta | KeyModifiers.Control),
                new KeyGesture(Key.Left, KeyModifiers.Meta | KeyModifiers.Control),
                new KeyGesture(Key.Right, KeyModifiers.Meta | KeyModifiers.Control)
            };

            return reserved.Select(Format).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }
}
