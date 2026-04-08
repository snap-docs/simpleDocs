using System;
using System.Diagnostics;
using System.Text;
using CodeExplainer.Engine.Models;
using CodeExplainer.Engine.Managers;

namespace CodeExplainer.Engine.Detectors
{
    public class ActiveWindowDetector
    {
        public ActiveWindowInfo GetForegroundEnvironment()
        {
            try
            {
                IntPtr hwnd = Win32Native.GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return CreateUnknown(hwnd);

                Win32Native.GetWindowThreadProcessId(hwnd, out uint processId);
                
                var process = Process.GetProcessById((int)processId);
                string processName = process.ProcessName;

                // Title
                StringBuilder titleBuilder = new StringBuilder(256);
                Win32Native.GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
                string title = titleBuilder.ToString();

                // Class Name
                StringBuilder classBuilder = new StringBuilder(256);
                Win32Native.GetClassName(hwnd, classBuilder, classBuilder.Capacity);
                string className = classBuilder.ToString();

                return new ActiveWindowInfo(hwnd, processId, processName, title, className);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ActiveWindowDetector] Error capturing foreground window: {ex.Message}");
                return CreateUnknown(IntPtr.Zero);
            }
        }

        private ActiveWindowInfo CreateUnknown(IntPtr hwnd)
        {
            return new ActiveWindowInfo(hwnd, 0, "unknown", "", "");
        }
    }
}
