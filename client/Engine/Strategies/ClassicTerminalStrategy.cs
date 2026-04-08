using System;
using System.Text;
using System.Threading.Tasks;
using CodeExplainer.Engine.Managers;
using CodeExplainer.Engine.Models;

namespace CodeExplainer.Engine.Strategies
{
    public class ClassicTerminalStrategy : ICaptureStrategy
    {
        private readonly ClipboardCompatibilityMode _compatibilityMode;

        public ClassicTerminalStrategy(ClipboardCompatibilityMode compatibilityMode)
        {
            _compatibilityMode = compatibilityMode;
        }

        public async Task<CaptureResult> CaptureAsync(ActiveWindowInfo window)
        {
            CapturePipelines.SelectedCaptureOutcome selected = await CapturePipelines.CaptureSelectedTextAsync(
                window,
                _compatibilityMode,
                preferMsaaFirst: false);

            CapturePipelines.BackgroundCaptureOutcome background = await CaptureClassicTerminalBackground(window);

            if (!selected.Success)
            {
                string status = $"{selected.Status} {background.Status}".Trim();
                return CaptureResult.Unsupported(
                    window,
                    EnvironmentType.ClassicTerminal,
                    selected.Method,
                    background.Method,
                    status,
                    background.Text);
            }

            bool isPartial = background.IsMetadataFallback;
            string combinedStatus = $"{selected.Status} {background.Status}".Trim();

            return new CaptureResult(
                selectedText: selected.Text,
                backgroundContext: background.Text,
                windowTitle: window.Title,
                processName: window.ProcessName,
                type: EnvironmentType.ClassicTerminal,
                selectedMethod: selected.Method,
                backgroundMethod: background.Method,
                isPartial: isPartial,
                isUnsupported: false,
                statusMessage: combinedStatus);
        }

        private static async Task<CapturePipelines.BackgroundCaptureOutcome> CaptureClassicTerminalBackground(ActiveWindowInfo window)
        {
            try
            {
                string? bufferText = TryReadVisibleConsoleBuffer(window);
                if (!string.IsNullOrWhiteSpace(bufferText))
                {
                    return new CapturePipelines.BackgroundCaptureOutcome
                    {
                        Text = bufferText,
                        Method = CaptureMethod.ConsoleBuffer,
                        IsMetadataFallback = false,
                        Status = "Classic terminal background captured via ReadConsoleOutput compatibility path."
                    };
                }
            }
            catch (Exception ex)
            {
                RuntimeLog.Warn("ClassicTerminal", $"ReadConsoleOutput compatibility path failed: {ex.Message}");
            }

            // OCR fallback – fires when console buffer access is denied or the terminal is hosted in a modern frame
            RuntimeLog.Info("ClassicTerminal", "Console buffer unavailable – attempting OCR fallback.");
            var ocrResult = await OcrCapture.CaptureWithConfidenceAsync(window, OcrCaptureArea.TerminalViewport);
            if (ocrResult.IsUsable(OcrCapture.TerminalThreshold))
            {
                RuntimeLog.Info("ClassicTerminal", $"OCR background succeeded ({ocrResult.Confidence:F2}).");
                return new CapturePipelines.BackgroundCaptureOutcome
                {
                    Text = ocrResult.Text,
                    Method = CaptureMethod.OcrVisualCapture,
                    IsMetadataFallback = false,
                    Status = $"Terminal background captured via OCR fallback (confidence {ocrResult.Confidence:F2})."
                };
            }

            return CapturePipelines.MetadataFallback(
                window,
                "Classic terminal background unavailable from console buffer or OCR; using window metadata.");
        }

        private static string? TryReadVisibleConsoleBuffer(ActiveWindowInfo window)
        {
            bool attached = false;
            try
            {
                Win32Native.FreeConsole();

                if (!Win32Native.AttachConsole(window.ProcessId))
                {
                    return null;
                }

                attached = true;
                IntPtr outputHandle = Win32Native.GetStdHandle(Win32Native.STD_OUTPUT_HANDLE);
                if (outputHandle == IntPtr.Zero || outputHandle == new IntPtr(-1))
                {
                    return null;
                }

                if (!Win32Native.GetConsoleScreenBufferInfo(outputHandle, out var bufferInfo))
                {
                    return null;
                }

                int width = bufferInfo.dwSize.X;
                short startY = bufferInfo.srWindow.Top;
                short endY = bufferInfo.srWindow.Bottom;
                int maxChars = 6000;

                var builder = new StringBuilder();
                for (short y = startY; y <= endY; y++)
                {
                    if (builder.Length >= maxChars)
                    {
                        break;
                    }

                    var line = new StringBuilder(width);
                    var coord = new Win32Native.COORD { X = 0, Y = y };
                    if (Win32Native.ReadConsoleOutputCharacter(outputHandle, line, (uint)width, coord, out _))
                    {
                        string trimmed = line.ToString().TrimEnd();
                        if (builder.Length > 0)
                        {
                            builder.AppendLine();
                        }

                        if (builder.Length + trimmed.Length > maxChars)
                        {
                            builder.Append(trimmed[..Math.Max(0, maxChars - builder.Length)]);
                            break;
                        }

                        builder.Append(trimmed);
                    }
                }

                string text = builder.ToString().Trim();
                return text.Length == 0 ? null : text;
            }
            finally
            {
                if (attached)
                {
                    Win32Native.FreeConsole();
                }
            }
        }
    }
}
