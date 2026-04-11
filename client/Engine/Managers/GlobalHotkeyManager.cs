using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using CodeExplainer;

namespace CodeExplainer.Engine.Managers
{
    public class GlobalHotkeyManager
    {
        private sealed record HotkeyBinding(uint Modifiers, uint Key, string Label);

        private const int HOTKEY_ID = 9000;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_NOREPEAT = 0x4000;
        private const uint VK_SPACE = 0x20;
        private const uint VK_E = 0x45;
        private const int WM_HOTKEY = 0x0312;
        public const string PreferredHotkeyLabel = "Ctrl+Shift+Space";

        private static readonly HotkeyBinding[] CandidateHotkeys =
        {
            new(MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_SPACE, PreferredHotkeyLabel),
            new(MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_SPACE, "Ctrl+Alt+Space"),
            new(MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_E, "Ctrl+Alt+E")
        };

        private readonly Window _window;
        private HwndSource? _source;
        private IntPtr _windowHandle;

        public event EventHandler? HotkeyPressed;
        public string RegisteredHotkeyLabel { get; private set; } = "Unavailable";
        public bool IsRegistered { get; private set; }

        public GlobalHotkeyManager(Window window)
        {
            _window = window;
        }

        public void Register()
        {
            Debug.WriteLine("[GlobalHotkeyManager] Register() called");

            var helper = new WindowInteropHelper(_window);
            helper.EnsureHandle();
            _windowHandle = helper.Handle;

            _source = HwndSource.FromHwnd(_windowHandle);
            _source?.AddHook(HwndHook);

            foreach (HotkeyBinding candidate in CandidateHotkeys)
            {
                bool registered = Win32Native.RegisterHotKey(_windowHandle, HOTKEY_ID, candidate.Modifiers, candidate.Key);
                if (registered)
                {
                    RegisteredHotkeyLabel = candidate.Label;
                    IsRegistered = true;
                    RuntimeLog.Info("Hotkey", $"Registered {candidate.Label}.");
                    Debug.WriteLine($"[GlobalHotkeyManager] {candidate.Label} hotkey registered SUCCESSFULLY.");
                    return;
                }

                uint error = Win32Native.GetLastError();
                RuntimeLog.Warn("Hotkey", $"Failed to register {candidate.Label} (Win32 error {error}).");
                Debug.WriteLine($"[GlobalHotkeyManager] Failed to register {candidate.Label} (Win32 error {error}).");
            }

            RegisteredHotkeyLabel = "Unavailable";
            IsRegistered = false;
            string attempted = string.Join(", ", CandidateHotkeys.Select(x => x.Label));
            string msg = $"FAILED to register any hotkey. Tried: {attempted}. They may already be in use by another application.";
            RuntimeLog.Error("Hotkey", msg);
            Debug.WriteLine($"[GlobalHotkeyManager] {msg}");
            MessageBox.Show(msg, "simpleDocs Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public void Unregister()
        {
            if (_windowHandle != IntPtr.Zero && IsRegistered)
            {
                Win32Native.UnregisterHotKey(_windowHandle, HOTKEY_ID);
            }

            IsRegistered = false;
            _source?.RemoveHook(HwndHook);
            _source = null;
            Debug.WriteLine("[GlobalHotkeyManager] Hotkey unregistered.");
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                RuntimeLog.Info("Hotkey", $"WM_HOTKEY received for {RegisteredHotkeyLabel}.");
                Debug.WriteLine("[GlobalHotkeyManager] HOTKEY TRIGGERED!");
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
                handled = true;
            }
            return IntPtr.Zero;
        }
    }
}
