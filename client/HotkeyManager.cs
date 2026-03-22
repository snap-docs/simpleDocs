using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CodeExplainer
{
    /// <summary>
    /// Registers Ctrl+Space as a global OS-level hotkey using Win32 RegisterHotKey.
    /// Works in any foreground application.
    /// </summary>
    public class HotkeyManager
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_ID = 9000;
        private const uint MOD_CONTROL = 0x0002;
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
            // Ensure the window has a handle
            var helper = new WindowInteropHelper(_window);
            helper.EnsureHandle();
            _windowHandle = helper.Handle;

            _source = HwndSource.FromHwnd(_windowHandle);
            _source?.AddHook(HwndHook);

            bool registered = RegisterHotKey(_windowHandle, HOTKEY_ID, MOD_CONTROL, VK_SPACE);
            if (!registered)
            {
                System.Diagnostics.Debug.WriteLine("Failed to register Ctrl+Space hotkey. It may be in use by another application.");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Ctrl+Space hotkey registered successfully.");
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
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
                handled = true;
            }
            return IntPtr.Zero;
        }
    }
}
