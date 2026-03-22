using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CodeExplainer
{
    /// <summary>
    /// Detects the active foreground application and classifies it
    /// as editor, browser, terminal, or unknown.
    /// </summary>
    public static class AppDetector
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        // Known process name → app type mappings
        private static readonly Dictionary<string, string> EditorProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            { "code", "editor" },
            { "cursor", "editor" },
            { "sublime_text", "editor" },
            { "devenv", "editor" },
            { "idea64", "editor" },
            { "pycharm64", "editor" },
            { "webstorm64", "editor" },
            { "notepad++", "editor" },
            { "notepad", "editor" }
        };

        private static readonly Dictionary<string, string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            { "chrome", "browser" },
            { "msedge", "browser" },
            { "firefox", "browser" },
            { "brave", "browser" },
            { "opera", "browser" }
        };

        private static readonly HashSet<string> TerminalProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "cmd",
            "powershell",
            "pwsh",
            "WindowsTerminal",
            "mintty",       // Git Bash
            "ConEmu64",
            "ConEmuC64"
        };

        // Apps where UIA is unreliable — go directly to clipboard fallback
        private static readonly HashSet<string> SkipUIAProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "cmd",
            "powershell",
            "pwsh",
            "WindowsTerminal",
            "mintty",
            "firefox"
        };

        /// <summary>
        /// Gets the foreground application's process name and classified type.
        /// </summary>
        public static (string processName, string appType) GetActiveApp()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                GetWindowThreadProcessId(hwnd, out uint processId);

                var process = Process.GetProcessById((int)processId);
                string processName = process.ProcessName;

                string appType = ClassifyProcess(processName);
                return (processName, appType);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AppDetector error: {ex.Message}");
                return ("unknown", "unknown");
            }
        }

        /// <summary>
        /// Returns true if UIA should be skipped for this process (go straight to clipboard).
        /// </summary>
        public static bool ShouldSkipUIA(string processName)
        {
            return SkipUIAProcesses.Contains(processName);
        }

        private static string ClassifyProcess(string processName)
        {
            if (EditorProcesses.ContainsKey(processName))
                return "editor";

            if (BrowserProcesses.ContainsKey(processName))
                return "browser";

            if (TerminalProcesses.Contains(processName))
                return "terminal";

            return "unknown";
        }
    }
}
