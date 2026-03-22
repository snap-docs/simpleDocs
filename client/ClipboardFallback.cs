using System;
using System.Threading.Tasks;
using System.Windows;
using WindowsInput;
using WindowsInput.Native;

namespace CodeExplainer
{
    /// <summary>
    /// Clipboard-based text capture fallback for apps where UIA doesn't work.
    /// Saves clipboard, simulates keystrokes, reads content, then restores clipboard.
    /// </summary>
    public static class ClipboardFallback
    {
        public record CaptureResult(string? SelectedText, string? FullContext);

        /// <summary>
        /// Captures text via clipboard manipulation. For terminals, uses Enter instead of Ctrl+C.
        /// Must run on the STA (UI) thread.
        /// </summary>
        public static async Task<CaptureResult> CaptureViaClipboard(bool isTerminal)
        {
            string? selectedText = null;
            string? fullContext = null;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                var sim = new InputSimulator();
                string? originalClipboard = null;

                try
                {
                    // Step 1: Save current clipboard content
                    if (Clipboard.ContainsText())
                    {
                        originalClipboard = Clipboard.GetText();
                    }
                    Clipboard.Clear();

                    // First, try to get selected text via Ctrl+C (or Enter for terminal)
                    if (isTerminal)
                    {
                        // Terminals: Enter copies selected text (Ctrl+C would send SIGINT)
                        sim.Keyboard.KeyPress(VirtualKeyCode.RETURN);
                    }
                    else
                    {
                        sim.Keyboard.ModifiedKeyStroke(
                            VirtualKeyCode.CONTROL,
                            VirtualKeyCode.VK_C);
                    }

                    await Task.Delay(150);

                    if (Clipboard.ContainsText())
                    {
                        selectedText = Clipboard.GetText();
                    }

                    // Step 2: Get full context via Ctrl+A then copy
                    Clipboard.Clear();

                    if (isTerminal)
                    {
                        // Terminal: Ctrl+A selects all, then Enter copies
                        sim.Keyboard.ModifiedKeyStroke(
                            VirtualKeyCode.CONTROL,
                            VirtualKeyCode.VK_A);
                        await Task.Delay(150);
                        sim.Keyboard.KeyPress(VirtualKeyCode.RETURN);
                    }
                    else
                    {
                        // Normal apps: Ctrl+A selects all, Ctrl+C copies
                        sim.Keyboard.ModifiedKeyStroke(
                            VirtualKeyCode.CONTROL,
                            VirtualKeyCode.VK_A);
                        await Task.Delay(150);
                        sim.Keyboard.ModifiedKeyStroke(
                            VirtualKeyCode.CONTROL,
                            VirtualKeyCode.VK_C);
                    }

                    await Task.Delay(150);

                    if (Clipboard.ContainsText())
                    {
                        fullContext = Clipboard.GetText();
                    }

                    // Step 3: Undo the select-all (Ctrl+Z) — not for terminals
                    if (!isTerminal)
                    {
                        sim.Keyboard.ModifiedKeyStroke(
                            VirtualKeyCode.CONTROL,
                            VirtualKeyCode.VK_Z);
                        await Task.Delay(50);
                    }
                    else
                    {
                        // Terminal: press Escape to deselect
                        sim.Keyboard.KeyPress(VirtualKeyCode.ESCAPE);
                        await Task.Delay(50);
                    }

                    // If no selected text was captured but we have full context,
                    // use full context as selected text too
                    if (string.IsNullOrEmpty(selectedText) && !string.IsNullOrEmpty(fullContext))
                    {
                        selectedText = fullContext;
                    }
                }
                finally
                {
                    // Step 4: Restore original clipboard — ALWAYS
                    try
                    {
                        if (originalClipboard != null)
                        {
                            Clipboard.SetText(originalClipboard);
                        }
                        else
                        {
                            Clipboard.Clear();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to restore clipboard: {ex.Message}");
                    }
                }
            });

            return new CaptureResult(selectedText, fullContext);
        }
    }
}
