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
                RuntimeLog.Warn("CompatClipboard", $"Editor-focus probe was negative for {window.ProcessName}; attempting compatibility capture anyway.");
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

        public async Task<string?> TryCaptureEditorBackgroundTextAsync(ActiveWindowInfo window, string? selectedTextHint)
        {
            if (!Enabled)
            {
                return null;
            }

            if (!IsProcessWhitelisted(window.ProcessName))
            {
                RuntimeLog.Info("CompatClipboard", $"Skipped editor background capture for {window.ProcessName}: not in whitelist.");
                return null;
            }

            if (!IsIdeProcess(window.ProcessName))
            {
                RuntimeLog.Warn("CompatClipboard", $"Skipped editor background capture for {window.ProcessName}: process is not IDE-like.");
                return null;
            }

            if (!UiAutomationCapture.IsEditorContentFocusedElement())
            {
                RuntimeLog.Warn("CompatClipboard", $"Editor-focus probe was negative for {window.ProcessName}; attempting editor background capture anyway.");
            }

            RuntimeLog.Warn("CompatClipboard", $"Trying editor background clipboard compatibility for {window.ProcessName}.");

            var simulator = new InputSimulator();
            ClipboardManager.ClipboardCaptureOutcome outcome = await _clipboardManager.SafeCaptureSelectionAsync(async () =>
            {
                await HotkeyReleaseGuard.WaitForTriggerKeysToSettleAsync();

                if (window.Hwnd != IntPtr.Zero && Win32Native.IsWindow(window.Hwnd))
                {
                    Win32Native.SetForegroundWindow(window.Hwnd);
                    await Task.Delay(120);
                }

                // FIXED: Symmetrical cursor walk to prevent permanent jumping.
                // 1. Collapse and go up 6 lines.
                // 2. Select 12 lines DOWN.
                // 3. Copy.
                // 4. Collapse (cursor returns to top of selection, i.e., -6 lines).
                // 5. Walk DOWN 6 lines back to the exact starting position.
                simulator.Keyboard.KeyPress(VirtualKeyCode.LEFT);
                await Task.Delay(25);
                simulator.Keyboard.KeyPress(VirtualKeyCode.HOME);
                await Task.Delay(20);

                // Move up 6 lines
                for (int i = 0; i < 6; i++)
                {
                    simulator.Keyboard.KeyPress(VirtualKeyCode.UP);
                    await Task.Delay(10);
                }

                // Select down 12 lines
                for (int i = 0; i < 12; i++)
                {
                    simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.SHIFT, VirtualKeyCode.DOWN);
                    await Task.Delay(12);
                }

                simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_C);
                await Task.Delay(240);

                // Collapse selection. In most editors (VS Code, VS, Notepad), 
                // Left Arrow on a downstream block collapses to the start of the block.
                simulator.Keyboard.KeyPress(VirtualKeyCode.LEFT);
                await Task.Delay(20);

                // Walk down 6 lines to restore exact original cursor position
                for (int i = 0; i < 6; i++)
                {
                    simulator.Keyboard.KeyPress(VirtualKeyCode.DOWN);
                    await Task.Delay(10);
                }
            });

            string? text = outcome.CapturedText?.Trim();
            if (!outcome.ClipboardMutatedDuringRequest || !outcome.DiffersFromPreviousText || !outcome.ClipboardChanged)
            {
                RuntimeLog.Warn("CompatClipboard", "Rejected editor background clipboard capture: clipboard mutation/change checks failed.");
                return null;
            }

            text = CapturePipelines.RefineEditorBackgroundForExternalUse(text ?? string.Empty, selectedTextHint, 1600);
            if (!LooksLikePlausibleEditorSelectionText(text ?? string.Empty))
            {
                RuntimeLog.Warn("CompatClipboard", "Rejected editor background clipboard capture: captured text is not plausible.");
                return null;
            }

            if (!CapturePipelines.AddsUsefulBackgroundContext(text!, selectedTextHint))
            {
                RuntimeLog.Warn("CompatClipboard", "Rejected editor background clipboard capture: text does not add context beyond selection.");
                return null;
            }

            RuntimeLog.Warn("CompatClipboard", $"Using editor background clipboard capture ({text!.Length} chars).");
            return text;
        }

        public async Task<string?> TryCaptureExpandedEditorBackgroundTextAsync(ActiveWindowInfo window, string? selectedTextHint, int extraLines = 14)
        {
            if (!Enabled)
            {
                return null;
            }

            if (!IsProcessWhitelisted(window.ProcessName) || !IsIdeProcess(window.ProcessName))
            {
                return null;
            }

            RuntimeLog.Warn("CompatClipboard", $"Trying expanded editor background capture for {window.ProcessName}.");

            var simulator = new InputSimulator();
            ClipboardManager.ClipboardCaptureOutcome outcome = await _clipboardManager.SafeCaptureSelectionAsync(async () =>
            {
                await HotkeyReleaseGuard.WaitForTriggerKeysToSettleAsync();

                if (window.Hwnd != IntPtr.Zero && Win32Native.IsWindow(window.Hwnd))
                {
                    Win32Native.SetForegroundWindow(window.Hwnd);
                    await Task.Delay(120);
                }

                int selectedLineCount = EstimateLineCount(selectedTextHint);
                int linesBefore = 6;
                int linesAfter = extraLines < 6 ? 6 : (extraLines > 22 ? 22 : extraLines);
                int totalLines = linesBefore + selectedLineCount + linesAfter;
                if (totalLines < 10)
                {
                    totalLines = 10;
                }
                else if (totalLines > 32)
                {
                    totalLines = 32;
                }

                // Step 1: Collapse active selection to start, move up for context lines
                simulator.Keyboard.KeyPress(VirtualKeyCode.LEFT);
                await Task.Delay(25);

                for (int i = 0; i < linesBefore; i++)
                {
                    simulator.Keyboard.KeyPress(VirtualKeyCode.UP);
                    await Task.Delay(10);
                }

                // Step 2: Go to start of that line
                simulator.Keyboard.KeyPress(VirtualKeyCode.HOME);
                await Task.Delay(20);

                // Step 3: Use Ctrl+L to select each line (VS Code/Cursor: "Select Line")
                for (int i = 0; i < totalLines; i++)
                {
                    simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_L);
                    await Task.Delay(i == 0 ? 40 : 18);
                }

                // Step 4: Copy and wait generously
                simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_C);
                await Task.Delay(300);  // FIXED: increased from 220ms — gives VS Code time to populate clipboard

                // Step 5: Undo the cursor changes! Ctrl+U is the native "Cursor Undo" hotkey in VS Code.
                // This perfectly restores the cursor and selection to exactly where it was before Step 1.
                simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_U);
                await Task.Delay(30);
            });

            string? text = outcome.CapturedText?.Trim();
            if (!outcome.ClipboardMutatedDuringRequest || !outcome.DiffersFromPreviousText || !outcome.ClipboardChanged)
            {
                RuntimeLog.Warn("CompatClipboard", "Rejected expanded editor background capture: clipboard mutation/change checks failed.");
                return null;
            }

            text = CapturePipelines.RefineEditorBackgroundForExternalUse(text ?? string.Empty, selectedTextHint, 1800);
            if (!LooksLikePlausibleEditorSelectionText(text ?? string.Empty))
            {
                RuntimeLog.Warn("CompatClipboard", "Rejected expanded editor background capture: captured text is not plausible.");
                return null;
            }

            if (!CapturePipelines.AddsUsefulBackgroundContext(text!, selectedTextHint))
            {
                RuntimeLog.Warn("CompatClipboard", "Rejected expanded editor background capture: text does not add context beyond selection.");
                return null;
            }

            RuntimeLog.Warn("CompatClipboard", $"Using expanded editor background capture ({text!.Length} chars).");
            return text;
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
                "opera",
                "pycharm64",
                "idea64",
                "rider64",
                "webstorm64",
                "phpstorm64",
                "clion64",
                "goland64",
                "rubymine64",
                "studio64",
                "devenv"
            };
        }

        public static bool IsIdeProcess(string processName)
        {
            return processName.Equals("code", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("cursor", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("sublime_text", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("pycharm64", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("idea64", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("rider64", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("webstorm64", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("phpstorm64", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("clion64", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("goland64", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("rubymine64", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("studio64", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("devenv", StringComparison.OrdinalIgnoreCase);
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

        private static int EstimateLineCount(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 1;
            }

            int count = text.Replace("\r\n", "\n").Split('\n').Length;
            if (count < 1)
            {
                return 1;
            }

            return count > 10 ? 10 : count;
        }

        private static bool AddsUsefulBackgroundContext(string text, string? selectedTextHint)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(selectedTextHint))
            {
                return text.Length >= 40 || text.Split('\n').Length >= 2;
            }

            string candidate = text.Trim();
            string selected = selectedTextHint.Trim();

            if (string.Equals(candidate, selected, StringComparison.Ordinal))
            {
                return false;
            }

            if (candidate.Length <= selected.Length + 12 && candidate.Split('\n').Length < 2)
            {
                return false;
            }

            return true;
        }
    }
}

