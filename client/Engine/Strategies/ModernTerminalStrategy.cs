using System.Threading.Tasks;
using CodeExplainer.Engine.Models;

namespace CodeExplainer.Engine.Strategies
{
    public class ModernTerminalStrategy : ICaptureStrategy
    {
        private readonly ClipboardCompatibilityMode _compatibilityMode;

        public ModernTerminalStrategy(ClipboardCompatibilityMode compatibilityMode)
        {
            _compatibilityMode = compatibilityMode;
        }

        public async Task<CaptureResult> CaptureAsync(ActiveWindowInfo window)
        {
            CapturePipelines.SelectedCaptureOutcome selected = await CapturePipelines.CaptureSelectedTextAsync(
                window,
                _compatibilityMode,
                preferMsaaFirst: false);

            CapturePipelines.BackgroundCaptureOutcome background = CapturePipelines.CaptureTerminalBackground(
                window,
                allowExperimentalDocumentRange: true);

            bool ocrUsed = false;
            float ocrConfidence = 0f;

            // ── OCR background fallback when UIA/MSAA terminal background unavailable ──
            if (background.IsMetadataFallback)
            {
                RuntimeLog.Info("ModernTerminal", "Terminal background is metadata-only – attempting OCR fallback.");
                var ocrBg = await OcrCapture.CaptureWithConfidenceAsync(window, OcrCaptureArea.TerminalViewport);
                if (ocrBg.IsUsable(OcrCapture.TerminalThreshold))
                {
                    RuntimeLog.Info("ModernTerminal", $"OCR background succeeded ({ocrBg.Confidence:F2}).");
                    background = new CapturePipelines.BackgroundCaptureOutcome
                    {
                        Text = ocrBg.Text,
                        Method = CaptureMethod.OcrVisualCapture,
                        IsMetadataFallback = false,
                        Status = $"Terminal background captured via OCR fallback (confidence {ocrBg.Confidence:F2})."
                    };
                    ocrUsed = true;
                    ocrConfidence = ocrBg.Confidence;
                }
            }

            // ── OCR selected-text fallback when primary selection fails ────────
            if (!selected.Success)
            {
                RuntimeLog.Info("ModernTerminal", "Terminal selection failed – attempting OCR selected-text fallback.");
                var ocrSel = await OcrCapture.CaptureWithConfidenceAsync(window, OcrCaptureArea.TerminalViewport);

                if (ocrSel.IsUsable(OcrCapture.SelectedTextThreshold))
                {
                    selected = new CapturePipelines.SelectedCaptureOutcome
                    {
                        Text   = ocrSel.Text,
                        Method = CaptureMethod.OcrVisualCapture,
                        Status = $"Terminal text captured via OCR fallback (confidence {ocrSel.Confidence:F2})."
                    };
                    ocrUsed = true;
                    ocrConfidence = System.Math.Max(ocrConfidence, ocrSel.Confidence);
                }
                else if (ocrSel.IsUsable(OcrCapture.BackgroundThreshold) && string.IsNullOrWhiteSpace(background.Text))
                {
                    background = new CapturePipelines.BackgroundCaptureOutcome
                    {
                        Text = ocrSel.Text,
                        Method = CaptureMethod.OcrVisualCapture,
                        IsMetadataFallback = false,
                        Status = $"Terminal background via OCR (medium confidence {ocrSel.Confidence:F2})."
                    };
                    ocrUsed = true;
                    ocrConfidence = System.Math.Max(ocrConfidence, ocrSel.Confidence);
                }
            }

            if (!selected.Success)
            {
                string status = $"{selected.Status} {background.Status}".Trim();
                return CaptureResult.Unsupported(
                    window,
                    EnvironmentType.ModernTerminal,
                    selected.Method,
                    background.Method,
                    status,
                    background.Text);
            }

            bool isPartial = background.IsMetadataFallback;
            string combinedStatus = $"{selected.Status} {background.Status}".Trim();

            return new CaptureResult(
                selectedText:      selected.Text,
                backgroundContext: background.Text,
                windowTitle:       window.Title,
                processName:       window.ProcessName,
                type:              EnvironmentType.ModernTerminal,
                selectedMethod:    selected.Method,
                backgroundMethod:  background.Method,
                isPartial:         isPartial,
                isUnsupported:     false,
                statusMessage:     combinedStatus,
                ocrUsed:           ocrUsed,
                ocrConfidence:     ocrConfidence);
        }
    }
}
