using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CodeExplainer
{
    /// <summary>
    /// Registers Win+Shift+Space as a global OS-level hotkey using Win32 RegisterHotKey.
    /// Works in any foreground application.
    /// </summary>
    public class HotkeyManager
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("kernel32.dll")]
        private static extern uint GetLastError();

        private const int HOTKEY_ID = 9000;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;
        private const uint VK_SPACE = 0x20;
        private const int WM_HOTKEY = 0x0312;

        private readonly Window _window;
        private HwndSource? _source;
        private IntPtr _windowHandle;

        public event EventHandler? HotkeyPressed;

        public HotkeyManager(Window window)
        {
            _window = window;
        }

        public void Register()
        {
            Console.WriteLine("[HotkeyManager] Register() called");

            // Ensure the window has a handle
            var helper = new WindowInteropHelper(_window);
            helper.EnsureHandle();
            _windowHandle = helper.Handle;
            Console.WriteLine($"[HotkeyManager] Window handle: {_windowHandle}");

            _source = HwndSource.FromHwnd(_windowHandle);
            Console.WriteLine($"[HotkeyManager] HwndSource: {(_source != null ? "OK" : "NULL")}");
            _source?.AddHook(HwndHook);

            bool registered = RegisterHotKey(_windowHandle, HOTKEY_ID, MOD_WIN | MOD_SHIFT | MOD_NOREPEAT, VK_SPACE);
            if (!registered)
            {
                uint error = GetLastError();
                string msg = $"[HotkeyManager] FAILED to register Win+Shift+Space hotkey (Win32 error {error}). It may be in use by another application.";
                Console.WriteLine(msg);
                MessageBox.Show(msg, "simpleDocs", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                Console.WriteLine("[HotkeyManager] Win+Shift+Space hotkey registered SUCCESSFULLY.");
            }
        }

        public void Unregister()
        {
            if (_windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(_windowHandle, HOTKEY_ID);
            }
            _source?.RemoveHook(HwndHook);
            _source = null;
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                Console.WriteLine($"[HotkeyManager] WM_HOTKEY received, wParam={wParam.ToInt32()}, expected={HOTKEY_ID}");
                if (wParam.ToInt32() == HOTKEY_ID)
                {
                    Console.WriteLine("[HotkeyManager] >>> HOTKEY TRIGGERED! Firing HotkeyPressed event.");
                    HotkeyPressed?.Invoke(this, EventArgs.Empty);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }
    }
}
