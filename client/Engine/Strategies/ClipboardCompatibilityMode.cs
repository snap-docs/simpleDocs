using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CodeExplainer.Engine.Managers;
using CodeExplainer.Engine.Models;
using WindowsInput;
using WindowsInput.Native;

namespace CodeExplainer.Engine.Strategies
{
    public sealed class ClipboardCompatibilityMode
    {
        private readonly ClipboardManager _clipboardManager;
        private readonly HashSet<string> _processWhitelist;

        public bool Enabled { get; }

        public ClipboardCompatibilityMode(ClipboardManager clipboardManager)
        {
            _clipboardManager = clipboardManager;
            Enabled = IsEnabledFromEnvironment();
            _processWhitelist = BuildWhitelist();
        }

        public bool IsProcessWhitelisted(string processName)
        {
            return _processWhitelist.Contains(processName);
        }

        public async Task<string?> TryCaptureSelectedTextAsync(ActiveWindowInfo window)
        {
            if (!Enabled)
            {
                return null;
            }

            if (!IsProcessWhitelisted(window.ProcessName))
            {
                RuntimeLog.Info("CompatClipboard", $"Skipped for {window.ProcessName}: not in whitelist.");
                return null;
            }

            if (IsIdeProcess(window.ProcessName) && !UiAutomationCapture.IsEditorContentFocusedElement())
            {
                RuntimeLog.Warn("CompatClipboard", $"Skipped for {window.ProcessName}: focused element is not editor content.");
                return null;
            }

            RuntimeLog.Warn("CompatClipboard", $"Using optional compatibility mode for {window.ProcessName}.");

            var simulator = new InputSimulator();
            ClipboardManager.ClipboardCaptureOutcome outcome = await _clipboardManager.SafeCaptureSelectionAsync(async () =>
            {
                await HotkeyReleaseGuard.WaitForTriggerKeysToSettleAsync();

                if (window.Hwnd != IntPtr.Zero && Win32Native.IsWindow(window.Hwnd))
                {
                    Win32Native.SetForegroundWindow(window.Hwnd);
                    await Task.Delay(120);
                }

                // Compatibility mode is selected-text-only and never uses Ctrl+A.
                simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_C);
                await Task.Delay(120);
            });

            string? text = outcome.CapturedText?.Trim();
            if (!outcome.ClipboardMutatedDuringRequest)
            {
                RuntimeLog.Warn("CompatClipboard", "Rejected capture: clipboard was not mutated during this request.");
                return null;
            }

            if (!outcome.DiffersFromPreviousText)
            {
                RuntimeLog.Warn("CompatClipboard", "Rejected capture: clipboard result matches previous clipboard content.");
                return null;
            }

            if (!outcome.ClipboardChanged)
            {
                RuntimeLog.Warn("CompatClipboard", "Rejected capture: clipboard changed flag is false.");
                return null;
            }

            if (!string.IsNullOrWhiteSpace(text) && LooksLikePlausibleEditorSelectionText(text))
            {
                RuntimeLog.Warn("CompatClipboard", $"Captured {text.Length} chars via compatibility mode.");
                return text;
            }

            RuntimeLog.Warn("CompatClipboard", "Compatibility mode did not capture plausible selected text.");
            return null;
        }

        public async Task<string?> TryCaptureSelectedTerminalTextAsync(ActiveWindowInfo window)
        {
            if (!IsProcessWhitelisted(window.ProcessName))
            {
                RuntimeLog.Info("CompatClipboard", $"Skipped for {window.ProcessName}: not in whitelist.");
                return null;
            }

            if (window.Hwnd != IntPtr.Zero && Win32Native.IsWindow(window.Hwnd))
            {
                Win32Native.SetForegroundWindow(window.Hwnd);
                await Task.Delay(80);
            }

            if (window.Hwnd != IntPtr.Zero && Win32Native.GetForegroundWindow() != window.Hwnd)
            {
                RuntimeLog.Warn("CompatClipboard", $"Skipped for {window.ProcessName}: window focus changed before terminal compatibility copy.");
                return null;
            }

            RuntimeLog.Warn("CompatClipboard", $"Using terminal compatibility mode for {window.ProcessName}.");

            var simulator = new InputSimulator();
            ClipboardManager.ClipboardCaptureOutcome outcome = await _clipboardManager.SafeCaptureSelectionAsync(async () =>
            {
                await HotkeyReleaseGuard.WaitForTriggerKeysToSettleAsync();

                if (window.Hwnd != IntPtr.Zero && Win32Native.IsWindow(window.Hwnd))
                {
                    Win32Native.SetForegroundWindow(window.Hwnd);
                    await Task.Delay(80);
                }

                simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_C);
                await Task.Delay(250);
            });

            if (window.Hwnd != IntPtr.Zero && Win32Native.GetForegroundWindow() != window.Hwnd)
            {
                RuntimeLog.Warn("CompatClipboard", "Rejected terminal capture: window focus changed during copy.");
                return null;
            }

            string? text = outcome.CapturedText?.Trim();
            if (!outcome.ClipboardMutatedDuringRequest)
            {
                RuntimeLog.Warn("CompatClipboard", "Rejected terminal capture: clipboard was not mutated during this request.");
                return null;
            }

            if (!outcome.DiffersFromPreviousText)
            {
                RuntimeLog.Warn("CompatClipboard", "Rejected terminal capture: clipboard result matches previous clipboard content.");
                return null;
            }

            if (!outcome.ClipboardChanged)
            {
                RuntimeLog.Warn("CompatClipboard", "Rejected terminal capture: clipboard changed flag is false.");
                return null;
            }

            if (!string.IsNullOrWhiteSpace(text) && LooksLikePlausibleTerminalSelectionText(text))
            {
                RuntimeLog.Warn("CompatClipboard", $"Captured {text.Length} chars via terminal compatibility mode.");
                return text;
            }

            RuntimeLog.Warn("CompatClipboard", "Terminal compatibility mode did not capture plausible terminal selected text.");
            return null;
        }

        private static bool IsEnabledFromEnvironment()
        {
            string? value = Environment.GetEnvironmentVariable("CODE_EXPLAINER_CLIPBOARD_COMPAT");
            if (string.IsNullOrWhiteSpace(value))
            {
                return true; // Default to true for the prototype so IDE code editor works
            }

            return value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || value.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<string> BuildWhitelist()
        {
            string? value = Environment.GetEnvironmentVariable("CODE_EXPLAINER_CLIPBOARD_COMPAT_WHITELIST");
            if (!string.IsNullOrWhiteSpace(value))
            {
                var envWhitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (string part in parts)
                {
                    envWhitelist.Add(part);
                }

                return envWhitelist;
            }

            // Default explicit whitelist (compat mode is still OFF by default).
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "code",
                "cursor",
                "sublime_text",
                "chrome",
                "msedge",
                "firefox",
                "brave",
                "opera"
            };
        }

        private static bool IsIdeProcess(string processName)
        {
            return processName.Equals("code", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("cursor", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("sublime_text", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikePlausibleEditorSelectionText(string text)
        {
            string normalized = text.Trim();
            if (normalized.Length == 0)
            {
                return false;
            }

            string lower = normalized.ToLowerInvariant();
            if (lower.Contains("vscode-file://")
                || lower.Contains("vscode-webview://")
                || lower.Contains("windowtitle:")
                || lower.Contains("processname:")
                || lower.Contains("the editor is not accessible")
                || lower.Contains("screen reader optimized mode"))
            {
                return false;
            }

            if (normalized.Length <= 120
                && (lower.Equals("explorer")
                    || lower.Equals("search")
                    || lower.Equals("source control")
                    || lower.Equals("run and debug")
                    || lower.Equals("extensions")
                    || lower.Equals("outline")
                    || lower.Equals("problems")
                    || lower.Equals("debug console")
                    || lower.Equals("terminal")
                    || lower.Equals("ports")))
            {
                return false;
            }

            return true;
        }

        private static bool LooksLikePlausibleTerminalSelectionText(string text)
        {
            string normalized = text.Trim();
            if (normalized.Length == 0)
            {
                return false;
            }

            string lower = normalized.ToLowerInvariant();
            if (lower.Contains("windowtitle:")
                || lower.Contains("processname:")
                || lower.Contains("vscode-file://")
                || lower.Contains("vscode-webview://")
                || lower.Contains("the editor is not accessible")
                || lower.Contains("screen reader optimized mode"))
            {
                return false;
            }

            if (normalized.Length <= 120
                && (lower.Equals("explorer")
                    || lower.Equals("search")
                    || lower.Equals("source control")
                    || lower.Equals("run and debug")
                    || lower.Equals("extensions")
                    || lower.Equals("outline")
                    || lower.Equals("problems")
                    || lower.Equals("debug console")
                    || lower.Equals("terminal")
                    || lower.Equals("ports")))
            {
                return false;
            }

            return true;
        }
    }
}
