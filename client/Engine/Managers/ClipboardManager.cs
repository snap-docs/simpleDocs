using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CodeExplainer;
using IDataObject = System.Windows.IDataObject;

namespace CodeExplainer.Engine.Managers
{
    public class ClipboardManager
    {
        public class ClipboardCaptureOutcome
        {
            public string? CapturedText { get; init; }
            public bool ClipboardChanged { get; init; }
            public bool ClipboardMutatedDuringRequest { get; init; }
            public bool DiffersFromPreviousText { get; init; }
        }

        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();

        /// <summary>
        /// Executes an action that modifies the clipboard, backing up and restoring
        /// the user's original clipboard content safely. 
        /// Runs on the STA thread.
        /// </summary>
        public async Task<ClipboardCaptureOutcome> SafeCaptureSelectionAsync(Func<Task> captureAction)
        {
            string? capturedText = null;
            bool clipboardChanged = false;
            bool clipboardMutatedDuringRequest = false;
            bool differsFromPreviousText = false;

            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                IDataObject? originalData = null;
                string? originalText = null;
                uint sequenceBeforeRequest = GetClipboardSequenceNumber();

                try
                {
                    // 1. Backup clipboard
                    originalData = GetClipboardDataWithRetry();
                    originalText = TryReadClipboardText();
                    ClearClipboardWithRetry();

                    // 2. Perform capture (e.g. simulate Ctrl+C)
                    await captureAction();

                    // 3. Read captured text with a short polling window.
                    // Some apps (especially browser surfaces) populate clipboard asynchronously.
                    capturedText = await WaitForClipboardTextAsync();
                    if (!string.IsNullOrWhiteSpace(capturedText))
                    {
                        differsFromPreviousText = !string.Equals(capturedText, originalText, StringComparison.Ordinal);
                        clipboardChanged = differsFromPreviousText;
                    }
                    else
                    {
                        RuntimeLog.Warn("Clipboard", "Timed out waiting for clipboard text after capture action.");
                    }

                    uint sequenceAfterCapture = GetClipboardSequenceNumber();
                    clipboardMutatedDuringRequest = sequenceAfterCapture != sequenceBeforeRequest;
                }
                catch (Exception ex)
                {
                    RuntimeLog.Error("Clipboard", $"Capture failed unexpectedly: {ex.Message}");
                    Debug.WriteLine($"[ClipboardManager] Capture failed: {ex.Message}");
                }
                finally
                {
                    // 4. Restore original clipboard
                    RestoreClipboardData(originalData);
                }
            }).Task.Unwrap();

            return new ClipboardCaptureOutcome
            {
                CapturedText = capturedText,
                ClipboardChanged = clipboardChanged,
                ClipboardMutatedDuringRequest = clipboardMutatedDuringRequest,
                DiffersFromPreviousText = differsFromPreviousText
            };
        }

        private static async Task<string?> WaitForClipboardTextAsync(int timeoutMs = 1200, int pollMs = 80)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                string? text = TryReadClipboardText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }

                await Task.Delay(pollMs);
            }

            return null;
        }

        private static string? TryReadClipboardText()
        {
            // Prefer Unicode text, but handle bad clipboard data defensively.
            // Some apps temporarily publish malformed/non-text clipboard payloads.
            for (int attempt = 1; attempt <= 4; attempt++)
            {
                try
                {
                    IDataObject? dataObject = Clipboard.GetDataObject();
                    if (dataObject == null)
                    {
                        return null;
                    }

                    if (dataObject.GetDataPresent(DataFormats.UnicodeText))
                    {
                        return dataObject.GetData(DataFormats.UnicodeText) as string;
                    }

                    if (dataObject.GetDataPresent(DataFormats.Text))
                    {
                        return dataObject.GetData(DataFormats.Text) as string;
                    }

                    if (dataObject.GetDataPresent(DataFormats.OemText))
                    {
                        return dataObject.GetData(DataFormats.OemText) as string;
                    }

                    return null;
                }
                catch (ExternalException ex)
                {
                    if (attempt == 1)
                    {
                        if (ex.HResult == unchecked((int)0x800401D3))
                        {
                            RuntimeLog.Warn("Clipboard", "Clipboard contained invalid data format. Retrying.");
                        }
                        else
                        {
                            RuntimeLog.Warn("Clipboard", $"Clipboard read unavailable ({ex.Message}). Retrying.");
                        }
                    }
                }

                Thread.Sleep(40);
            }

            return null;
        }

        private IDataObject? GetClipboardDataWithRetry(int maxRetries = 5, int delayMs = 50)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    return Clipboard.GetDataObject();
                }
                catch (ExternalException)
                {
                    RuntimeLog.Warn("Clipboard", $"Clipboard locked on backup, retrying {i + 1}/{maxRetries}.");
                    Debug.WriteLine($"[ClipboardManager] Clipboard locked on backup, retrying {i + 1}/{maxRetries}...");
                    Thread.Sleep(delayMs);
                }
            }
            return null;
        }

        private static void ClearClipboardWithRetry(int maxRetries = 5, int delayMs = 40)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    Clipboard.Clear();
                    return;
                }
                catch (ExternalException ex)
                {
                    if (ex.HResult == unchecked((int)0x800401D3))
                    {
                        // Invalid existing data; treat as already cleared enough for capture flow.
                        RuntimeLog.Warn("Clipboard", "Clipboard had invalid data while clearing. Continuing capture.");
                        return;
                    }

                    Thread.Sleep(delayMs);
                }
            }

            RuntimeLog.Warn("Clipboard", "Failed to clear clipboard after max retries.");
        }

        private void RestoreClipboardData(IDataObject? data)
        {
            if (data == null)
            {
                ClearClipboardWithRetry();
                return;
            }

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Clipboard.SetDataObject(data, copy: true);
                    return;
                }
                catch (ExternalException)
                {
                    RuntimeLog.Warn("Clipboard", $"Clipboard locked on restore, retrying {i + 1}/5.");
                    Debug.WriteLine($"[ClipboardManager] Clipboard locked on restore, retrying {i + 1}/5...");
                    Thread.Sleep(50);
                }
            }
            RuntimeLog.Warn("Clipboard", "Failed to restore clipboard after max retries.");
            Debug.WriteLine("[ClipboardManager] FAILED to restore clipboard after max retries.");
        }
    }
}
