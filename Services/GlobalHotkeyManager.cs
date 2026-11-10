using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Input;
using SharpHook;
using SharpHook.Data;
using SharpHook.Native;

namespace BoomBx.Services
{
    /// <summary>
    /// System-wide keyboard hook (SharpHook) used to trigger BoomBx soundboard actions
    /// while another application owns the focus.
    ///
    /// The hook never activates or raises the BoomBx window: it only observes the
    /// keyboard, so playback can start in the background.
    ///
    /// Hotkey actions are raised on the SharpHook worker thread. Callers must marshal
    /// to the UI thread themselves (BoomBx posts through Avalonia's Dispatcher).
    /// </summary>
    public sealed class GlobalHotkeyManager : IDisposable
    {
        private readonly TaskPoolGlobalHook _hook = new();
        private readonly object _gate = new();
        private readonly Dictionary<Hotkey, HotkeyBinding> _bindings = new();
        private readonly HashSet<KeyCode> _pressedKeys = new();

        private Task? _hookTask;
        private volatile bool _enabled = true;
        private volatile bool _disposed;
        private bool _started;

        /// <summary>
        /// Requests hotkeys to be ignored without tearing the hook down (used while the
        /// user is capturing a new combination, or when the user disables hotkeys).
        /// </summary>
        public bool IsEnabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        /// <summary>True when the keyboard hook started successfully.</summary>
        public bool IsRunning
        {
            get
            {
                lock (_gate)
                {
                    return _started && !_disposed;
                }
            }
        }

        public event EventHandler<HotkeyTriggeredEventArgs>? HotkeyTriggered;

        /// <summary>
        /// Starts the low-level hook. Returns false with a human readable reason when the
        /// platform refuses to install it (for example when the OS denies input taps).
        /// </summary>
        public bool TryStart(out string? error)
        {
            error = null;

            lock (_gate)
            {
                if (_disposed)
                {
                    error = "the hotkey manager has been disposed";
                    return false;
                }

                if (_started)
                {
                    return true;
                }

                try
                {
                    _hook.KeyPressed += OnKeyPressed;
                    _hook.KeyReleased += OnKeyReleased;

                    _hookTask = _hook.RunAsync();
                    _ = _hookTask.ContinueWith(
                        task => Logger.Log($"Global hotkey hook stopped unexpectedly: {task.Exception}"),
                        TaskContinuationOptions.OnlyOnFaulted);

                    _started = true;
                    return true;
                }
                catch (Exception ex)
                {
                    _hook.KeyPressed -= OnKeyPressed;
                    _hook.KeyReleased -= OnKeyReleased;
                    error = ex.Message;
                    Logger.Log($"Global hotkey hook failed to start: {ex}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Adds or replaces a single combination. <see cref="SetBindings"/> is preferred when
        /// the whole hotkey set is rebuilt at once; this overload keeps incremental callers simple.
        /// </summary>
        public void RegisterHotkey(KeyGesture gesture, Action action, string? label = null)
        {
            if (gesture is null || action is null)
            {
                return;
            }

            var key = new Hotkey(ConvertModifiers(gesture.KeyModifiers), ConvertKey(gesture.Key));
            if (key.Key == KeyCode.VcUndefined)
            {
                return;
            }

            lock (_gate)
            {
                _bindings[key] = new HotkeyBinding(gesture, label ?? HotkeyText.Format(gesture), action);
            }
        }

        /// <summary>
        /// Replaces every registered combination at once. Safe to call from the UI thread
        /// while the hook is firing: the hook thread only reads a snapshot under a lock.
        /// </summary>
        public void SetBindings(IEnumerable<HotkeyBinding> bindings)
        {
            var next = new Dictionary<Hotkey, HotkeyBinding>();

            foreach (var binding in bindings)
            {
                if (binding?.Gesture is null || binding.Action is null)
                {
                    continue;
                }

                var key = new Hotkey(ConvertModifiers(binding.Gesture.KeyModifiers), ConvertKey(binding.Gesture.Key));

                // KeyCode.VcUndefined means Avalonia's Key could not be mapped to a
                // physical key; registering it would never match.
                if (key.Key == KeyCode.VcUndefined)
                {
                    continue;
                }

                next[key] = binding;
            }

            lock (_gate)
            {
                _bindings.Clear();
                foreach (var pair in next)
                {
                    _bindings[pair.Key] = pair.Value;
                }
            }
        }

        /// <summary>Removes every registered combination (the hook stays alive).</summary>
        public void UnregisterAll()
        {
            lock (_gate)
            {
                _bindings.Clear();
            }
        }

        private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            // KeyPressed is delivered again while a key is held down: only the first
            // press may trigger an action, otherwise holding a hotkey would loop the sound.
            bool firstPress = _pressedKeys.Add(e.Data.KeyCode);
            if (!firstPress || !_enabled)
            {
                return;
            }

            TryTriggerHotkey(e.Data.KeyCode);
        }

        private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
        {
            _pressedKeys.Remove(e.Data.KeyCode);
        }

        private void TryTriggerHotkey(KeyCode key)
        {
            // A modifier alone can never be a complete hotkey.
            if (IsModifierKey(key))
            {
                return;
            }

            var hotkey = new Hotkey(CurrentModifiers(), key);

            HotkeyBinding? binding;
            lock (_gate)
            {
                _bindings.TryGetValue(hotkey, out binding);
            }

            if (binding is null)
            {
                return;
            }

            try
            {
                HotkeyTriggered?.Invoke(
                    this,
                    new HotkeyTriggeredEventArgs(binding.Label, HotkeyText.Format(binding.Gesture)));
            }
            catch (Exception ex)
            {
                // The UI subscriber only posts to the dispatcher, but a listener must never
                // be able to kill the hook thread.
                Logger.Log($"Hotkey notification failed: {ex}");
            }

            try
            {
                binding.Action();
            }
            catch (Exception ex)
            {
                Logger.Log($"Hotkey action '{binding.Label}' failed: {ex}");
            }
        }

        private HotkeyModifiers CurrentModifiers()
        {
            var modifiers = HotkeyModifiers.None;

            if (_pressedKeys.Contains(KeyCode.VcLeftControl) || _pressedKeys.Contains(KeyCode.VcRightControl))
            {
                modifiers |= HotkeyModifiers.Control;
            }

            if (_pressedKeys.Contains(KeyCode.VcLeftShift) || _pressedKeys.Contains(KeyCode.VcRightShift))
            {
                modifiers |= HotkeyModifiers.Shift;
            }

            if (_pressedKeys.Contains(KeyCode.VcLeftAlt) || _pressedKeys.Contains(KeyCode.VcRightAlt))
            {
                modifiers |= HotkeyModifiers.Alt;
            }

            if (_pressedKeys.Contains(KeyCode.VcLeftMeta) || _pressedKeys.Contains(KeyCode.VcRightMeta))
            {
                modifiers |= HotkeyModifiers.Meta;
            }

            return modifiers;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _enabled = false;
                _bindings.Clear();
            }

            // Detach first so a release arriving during teardown cannot touch a half
            // disposed hook, then stop the native hook and release its resources.
            try
            {
                _hook.KeyPressed -= OnKeyPressed;
                _hook.KeyReleased -= OnKeyReleased;
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to detach hotkey events: {ex}");
            }

            try
            {
                _hook.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to dispose global hotkey hook: {ex}");
            }

            GC.SuppressFinalize(this);
        }

        public sealed record HotkeyBinding(KeyGesture Gesture, string Label, Action Action);

        public sealed class HotkeyTriggeredEventArgs : EventArgs
        {
            public HotkeyTriggeredEventArgs(string label, string gesture)
            {
                Label = label;
                Gesture = gesture;
            }

            public string Label { get; }

            public string Gesture { get; }
        }

        private static HotkeyModifiers ConvertModifiers(KeyModifiers modifiers)
        {
            var result = HotkeyModifiers.None;
            if (modifiers.HasFlag(KeyModifiers.Alt)) result |= HotkeyModifiers.Alt;
            if (modifiers.HasFlag(KeyModifiers.Control)) result |= HotkeyModifiers.Control;
            if (modifiers.HasFlag(KeyModifiers.Shift)) result |= HotkeyModifiers.Shift;
            if (modifiers.HasFlag(KeyModifiers.Meta)) result |= HotkeyModifiers.Meta;
            return result;
        }

        [Flags]
        private enum HotkeyModifiers : ushort
        {
            None = 0,
            Alt = 1,
            Control = 2,
            Shift = 4,
            Meta = 8
        }

        private static bool IsModifierKey(KeyCode key) => key is
            KeyCode.VcLeftControl or KeyCode.VcRightControl or
            KeyCode.VcLeftShift or KeyCode.VcRightShift or
            KeyCode.VcLeftAlt or KeyCode.VcRightAlt or
            KeyCode.VcLeftMeta or KeyCode.VcRightMeta or
            KeyCode.VcCapsLock or KeyCode.VcNumLock or KeyCode.VcScrollLock;

        private readonly record struct Hotkey(HotkeyModifiers Modifiers, KeyCode Key);

        private static KeyCode ConvertKey(Key key)
        {
            return key switch
            {
                Key.F1 => KeyCode.VcF1,
                Key.F2 => KeyCode.VcF2,
                Key.F3 => KeyCode.VcF3,
                Key.F4 => KeyCode.VcF4,
                Key.F5 => KeyCode.VcF5,
                Key.F6 => KeyCode.VcF6,
                Key.F7 => KeyCode.VcF7,
                Key.F8 => KeyCode.VcF8,
                Key.F9 => KeyCode.VcF9,
                Key.F10 => KeyCode.VcF10,
                Key.F11 => KeyCode.VcF11,
                Key.F12 => KeyCode.VcF12,
                Key.F13 => KeyCode.VcF13,
                Key.F14 => KeyCode.VcF14,
                Key.F15 => KeyCode.VcF15,
                Key.F16 => KeyCode.VcF16,
                Key.F17 => KeyCode.VcF17,
                Key.F18 => KeyCode.VcF18,
                Key.F19 => KeyCode.VcF19,
                Key.F20 => KeyCode.VcF20,
                Key.F21 => KeyCode.VcF21,
                Key.F22 => KeyCode.VcF22,
                Key.F23 => KeyCode.VcF23,
                Key.F24 => KeyCode.VcF24,
                Key.Left => KeyCode.VcLeft,
                Key.Right => KeyCode.VcRight,
                Key.Up => KeyCode.VcUp,
                Key.Down => KeyCode.VcDown,
                Key.Space => KeyCode.VcSpace,
                Key.Tab => KeyCode.VcTab,
                Key.Escape => KeyCode.VcEscape,
                Key.Enter => KeyCode.VcEnter,
                Key.Back => KeyCode.VcBackspace,
                Key.Delete => KeyCode.VcDelete,
                Key.Insert => KeyCode.VcInsert,
                Key.Home => KeyCode.VcHome,
                Key.End => KeyCode.VcEnd,
                Key.PageUp => KeyCode.VcPageUp,
                Key.PageDown => KeyCode.VcPageDown,
                Key.A => KeyCode.VcA,
                Key.B => KeyCode.VcB,
                Key.C => KeyCode.VcC,
                Key.D => KeyCode.VcD,
                Key.E => KeyCode.VcE,
                Key.F => KeyCode.VcF,
                Key.G => KeyCode.VcG,
                Key.H => KeyCode.VcH,
                Key.I => KeyCode.VcI,
                Key.J => KeyCode.VcJ,
                Key.K => KeyCode.VcK,
                Key.L => KeyCode.VcL,
                Key.M => KeyCode.VcM,
                Key.N => KeyCode.VcN,
                Key.O => KeyCode.VcO,
                Key.P => KeyCode.VcP,
                Key.Q => KeyCode.VcQ,
                Key.R => KeyCode.VcR,
                Key.S => KeyCode.VcS,
                Key.T => KeyCode.VcT,
                Key.U => KeyCode.VcU,
                Key.V => KeyCode.VcV,
                Key.W => KeyCode.VcW,
                Key.X => KeyCode.VcX,
                Key.Y => KeyCode.VcY,
                Key.Z => KeyCode.VcZ,
                Key.D0 => KeyCode.Vc0,
                Key.D1 => KeyCode.Vc1,
                Key.D2 => KeyCode.Vc2,
                Key.D3 => KeyCode.Vc3,
                Key.D4 => KeyCode.Vc4,
                Key.D5 => KeyCode.Vc5,
                Key.D6 => KeyCode.Vc6,
                Key.D7 => KeyCode.Vc7,
                Key.D8 => KeyCode.Vc8,
                Key.D9 => KeyCode.Vc9,
                Key.NumPad0 => KeyCode.VcNumPad0,
                Key.NumPad1 => KeyCode.VcNumPad1,
                Key.NumPad2 => KeyCode.VcNumPad2,
                Key.NumPad3 => KeyCode.VcNumPad3,
                Key.NumPad4 => KeyCode.VcNumPad4,
                Key.NumPad5 => KeyCode.VcNumPad5,
                Key.NumPad6 => KeyCode.VcNumPad6,
                Key.NumPad7 => KeyCode.VcNumPad7,
                Key.NumPad8 => KeyCode.VcNumPad8,
                Key.NumPad9 => KeyCode.VcNumPad9,
                Key.Multiply => KeyCode.VcNumPadMultiply,
                Key.Add => KeyCode.VcNumPadAdd,
                Key.Subtract => KeyCode.VcNumPadSubtract,
                Key.Decimal => KeyCode.VcNumPadDecimal,
                Key.Divide => KeyCode.VcNumPadDivide,
                Key.OemSemicolon => KeyCode.VcSemicolon,
                Key.OemComma => KeyCode.VcComma,
                Key.OemPeriod => KeyCode.VcPeriod,
                Key.OemQuestion => KeyCode.VcSlash,
                Key.OemTilde => KeyCode.VcBackQuote,
                Key.OemOpenBrackets => KeyCode.VcOpenBracket,
                Key.OemCloseBrackets => KeyCode.VcCloseBracket,
                Key.OemPipe => KeyCode.VcBackslash,
                Key.OemQuotes => KeyCode.VcQuote,
                Key.OemMinus => KeyCode.VcMinus,
                Key.OemPlus => KeyCode.VcEquals,
                _ => KeyCode.VcUndefined
            };
        }
    }
}
