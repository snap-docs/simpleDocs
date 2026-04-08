using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.Ocr;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using CodeExplainer.Engine.Models;

namespace CodeExplainer.Engine.Strategies
{
    /// <summary>Specifies which area of the window to screenshot for OCR.</summary>
    internal enum OcrCaptureArea
    {
        /// <summary>Entire window. Used for browsers/unknown apps.</summary>
        FullWindow,
        /// <summary>Editor viewport: strip top chrome and side file-tree.</summary>
        EditorViewport,
        /// <summary>Terminal viewport: strip top chrome only.</summary>
        TerminalViewport
    }

    /// <summary>Result returned by OcrCapture with confidence metadata.</summary>
    internal readonly struct OcrResult
    {
        public string Text { get; }
        /// <summary>0.0 – 1.0 composite confidence heuristic.</summary>
        public float Confidence { get; }
        public OcrCaptureArea Area { get; }

        public OcrResult(string text, float confidence, OcrCaptureArea area)
        {
            Text = text;
            Confidence = confidence;
            Area = area;
        }

        /// <summary>Returns true when the result has usable text above the confidence threshold.</summary>
        public bool IsUsable(float minConfidence = 0.45f)
            => !string.IsNullOrWhiteSpace(Text) && Confidence >= minConfidence;

        public static OcrResult Empty => new OcrResult(string.Empty, 0f, OcrCaptureArea.FullWindow);
    }

    internal static class OcrCapture
    {
        private static readonly OcrEngine? _engine = OcrEngine.TryCreateFromUserProfileLanguages();

        // ─── Confidence thresholds ─────────────────────────────────────────────
        public const float SelectedTextThreshold  = 0.75f;  // OCR → selected_text
        public const float BackgroundThreshold    = 0.45f;  // OCR → background_context
        public const float TerminalThreshold      = 0.40f;  // Terminals render cleanly

        // ─── P/Invoke ─────────────────────────────────────────────────────────
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        // ─── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Full OCR with confidence scoring and viewport cropping.
        /// This is the primary method used by all strategies.
        /// </summary>
        public static async Task<OcrResult> CaptureWithConfidenceAsync(
            ActiveWindowInfo window,
            OcrCaptureArea area = OcrCaptureArea.FullWindow)
        {
            try
            {
                if (!GetWindowRect(window.Hwnd, out RECT rect))
                {
                    RuntimeLog.Warn("OcrCapture", "GetWindowRect failed.");
                    return OcrResult.Empty;
                }

                int fullWidth  = rect.Right  - rect.Left;
                int fullHeight = rect.Bottom - rect.Top;
                if (fullWidth <= 0 || fullHeight <= 0) return OcrResult.Empty;

                // Apply viewport crop based on area type
                var crop = ComputeCropRect(rect, fullWidth, fullHeight, area);

                using var bmp = new Bitmap(crop.Width, crop.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(crop.X, crop.Y, 0, 0,
                        new Size(crop.Width, crop.Height), CopyPixelOperation.SourceCopy);
                }

                using var softBmp = ConvertToSoftwareBitmap(bmp);
                if (_engine == null)
                {
                    RuntimeLog.Warn("OcrCapture", "OcrEngine unavailable – no language pack.");
                    return OcrResult.Empty;
                }

                var ocrResult = await _engine.RecognizeAsync(softBmp);
                string rawText = ocrResult.Text ?? string.Empty;

                if (string.IsNullOrWhiteSpace(rawText))
                {
                    RuntimeLog.Info("OcrCapture", $"OCR returned empty for '{window.Title}' ({area}).");
                    return OcrResult.Empty;
                }

                float confidence = ComputeConfidence(ocrResult, crop.Width, crop.Height);
                string cleaned   = CleanOcrText(rawText);

                RuntimeLog.Info("OcrCapture",
                    $"OCR: area={area} chars={cleaned.Length} confidence={confidence:F2} title='{window.Title}'");

                return new OcrResult(cleaned, confidence, area);
            }
            catch (Exception ex)
            {
                RuntimeLog.Warn("OcrCapture", $"OCR failed: {ex.Message}");
                return OcrResult.Empty;
            }
        }

        /// <summary>
        /// Legacy simple capture used by BrowserStrategy (no confidence scoring, full window).
        /// Kept for backward compatibility.
        /// </summary>
        public static async Task<string?> CaptureAsync(ActiveWindowInfo window)
        {
            var result = await CaptureWithConfidenceAsync(window, OcrCaptureArea.FullWindow);
            return result.IsUsable(0.0f) ? result.Text : null;
        }

        // ─── Viewport crop ─────────────────────────────────────────────────────

        private static System.Drawing.Rectangle ComputeCropRect(
            RECT rect, int fullWidth, int fullHeight, OcrCaptureArea area)
        {
            switch (area)
            {
                case OcrCaptureArea.EditorViewport:
                {
                    // Strip top chrome (title bar + menu bar ≈ 80px)
                    // Strip right side (file explorer / minimap ≈ 22% of width)
                    int topTrim   = Math.Min(80, fullHeight / 6);
                    int rightTrim = (int)(fullWidth * 0.22);
                    int x = rect.Left;
                    int y = rect.Top + topTrim;
                    int w = fullWidth - rightTrim;
                    int h = fullHeight - topTrim;
                    return new System.Drawing.Rectangle(x, y, Math.Max(w, 1), Math.Max(h, 1));
                }
                case OcrCaptureArea.TerminalViewport:
                {
                    // Strip top chrome (title bar + tab strip ≈ 60px)
                    int topTrim = Math.Min(60, fullHeight / 8);
                    return new System.Drawing.Rectangle(
                        rect.Left, rect.Top + topTrim,
                        fullWidth, Math.Max(fullHeight - topTrim, 1));
                }
                default: // FullWindow
                    return new System.Drawing.Rectangle(rect.Left, rect.Top, fullWidth, fullHeight);
            }
        }

        // ─── Confidence heuristic (uses WinRT OcrResult directly) ──────────────

        private static float ComputeConfidence(Windows.Media.Ocr.OcrResult ocrResult, int width, int height)
        {
            var lines = ocrResult.Lines;
            if (lines == null || lines.Count == 0) return 0f;

            int totalWords     = 0;
            int plausibleLines = 0;
            foreach (var line in lines)
            {
                int wordCount = line.Words?.Count ?? 0;
                totalWords += wordCount;
                if (wordCount >= 2) plausibleLines++;
            }

            float pixelDensity  = (float)(totalWords * 10_000) / Math.Max(width * height, 1);
            float densityScore  = Math.Min(pixelDensity / 2.5f, 1.0f);
            float structureScore = (float)plausibleLines / Math.Max(lines.Count, 1);
            return Math.Clamp((densityScore * 0.4f) + (structureScore * 0.6f), 0f, 1f);
        }

        // ─── Text cleanup ──────────────────────────────────────────────────────

        private static string CleanOcrText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var sb = new StringBuilder();
            string[] lines = raw.Split('\n');
            int blankRun = 0;

            foreach (string line in lines)
            {
                string trimmed = line.TrimEnd();

                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    blankRun++;
                    // Collapse runs of ≥ 3 blank lines to 2
                    if (blankRun <= 2) sb.AppendLine();
                    continue;
                }

                blankRun = 0;

                // Discard isolated single-character lines (common OCR artefacts from UI chrome)
                if (trimmed.TrimStart().Length == 1 && !char.IsDigit(trimmed[0]))
                    continue;

                sb.AppendLine(trimmed);
            }

            return sb.ToString().Trim();
        }

        // ─── Bitmap → SoftwareBitmap conversion ───────────────────────────────

        private static SoftwareBitmap ConvertToSoftwareBitmap(Bitmap bmp)
        {
            var softBmp = new SoftwareBitmap(BitmapPixelFormat.Bgra8, bmp.Width, bmp.Height, BitmapAlphaMode.Ignore);
            var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            
            int byteCount = Math.Abs(data.Stride) * bmp.Height;
            byte[] bgraData = new byte[byteCount];
            Marshal.Copy(data.Scan0, bgraData, 0, byteCount);
            bmp.UnlockBits(data);
            
            softBmp.CopyFromBuffer(bgraData.AsBuffer());
            return softBmp;
        }
    }
}
