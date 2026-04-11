using System.Threading.Tasks;
using CodeExplainer.Engine.Models;

namespace CodeExplainer.Engine.Strategies
{
    public class IDEStrategy : ICaptureStrategy
    {
        private readonly ClipboardCompatibilityMode _compatibilityMode;

        public IDEStrategy(ClipboardCompatibilityMode compatibilityMode)
        {
            _compatibilityMode = compatibilityMode;
        }

        public async Task<CaptureResult> CaptureAsync(ActiveWindowInfo window)
        {
            bool embeddedTerminalFocused = UiAutomationCapture.IsTerminalFocusedElement();
            if (embeddedTerminalFocused)
            {
                RuntimeLog.Warn("IDE", "Embedded terminal focus detected. Rerouting to IDE terminal capture path.");
                return await CaptureIdeEmbeddedTerminalAsync(window);
            }

            return await CaptureIdeEditorAsync(window);
        }

        // ─── IDE Editor ────────────────────────────────────────────────────────

        private async Task<CaptureResult> CaptureIdeEditorAsync(ActiveWindowInfo window)
        {
            CapturePipelines.SelectedCaptureOutcome selected = await CapturePipelines.CaptureSelectedTextAsync(
                window,
                _compatibilityMode,
                preferMsaaFirst: false,
                allowMsaaFocusedFallback: false);

            CapturePipelines.BackgroundCaptureOutcome background =
                CapturePipelines.CaptureEditorBackground(window, maxChars: 10000, selectedTextHint: selected.Text);

            if (ShouldDemoteWeakMsaaSelection(selected, background))
            {
                selected = new CapturePipelines.SelectedCaptureOutcome
                {
                    Method = CaptureMethod.Unsupported,
                    Status = "Selected text was demoted because weak MSAA selected/background sources heavily overlapped."
                };
            }

            // ── OCR selected-text fallback ─────────────────────────────────────
            bool ocrUsed = false;
            float ocrConfidence = 0f;

            if (!selected.Success)
            {
                RuntimeLog.Info("IDE", "UIA/MSAA/clipboard all failed – attempting OCR selected-text fallback for editor.");
                var ocrResult = await OcrCapture.CaptureWithConfidenceAsync(window, OcrCaptureArea.EditorViewport);

                if (ocrResult.IsUsable(OcrCapture.SelectedTextThreshold))
                {
                    // High-confidence OCR → promote to selected_text
                    selected = new CapturePipelines.SelectedCaptureOutcome
                    {
                        Text   = ocrResult.Text,
                        Method = CaptureMethod.OcrVisualCapture,
                        Status = $"Selected text captured via OCR fallback (confidence {ocrResult.Confidence:F2})."
                    };
                    ocrUsed = true;
                    ocrConfidence = ocrResult.Confidence;
                }
                else if (ocrResult.IsUsable(OcrCapture.BackgroundThreshold))
                {
                    // Medium-confidence OCR → use as background only
                    RuntimeLog.Info("IDE", $"OCR medium confidence ({ocrResult.Confidence:F2}) – using as background_context only.");
                    if (string.IsNullOrWhiteSpace(background.Text))
                    {
                        background = new CapturePipelines.BackgroundCaptureOutcome
                        {
                            Text   = ocrResult.Text,
                            Method = CaptureMethod.OcrVisualCapture,
                            Status = $"Background captured via OCR fallback (confidence {ocrResult.Confidence:F2}).",
                            IsMetadataFallback = false
                        };
                    }
                    ocrUsed = true;
                    ocrConfidence = ocrResult.Confidence;
                }
                else
                {
                    RuntimeLog.Warn("IDE", $"OCR confidence too low ({ocrResult.Confidence:F2}) – discarding OCR result.");
                }
            }

            // ── OCR background fallback (when background is only metadata) ─────
            if (!ocrUsed && background.IsMetadataFallback)
            {
                RuntimeLog.Info("IDE", "Editor background is metadata-only – attempting OCR background fallback.");
                var ocrBg = await OcrCapture.CaptureWithConfidenceAsync(window, OcrCaptureArea.EditorViewport);
                if (ocrBg.IsUsable(OcrCapture.BackgroundThreshold))
                {
                    background = new CapturePipelines.BackgroundCaptureOutcome
                    {
                        Text   = ocrBg.Text,
                        Method = CaptureMethod.OcrVisualCapture,
                        Status = $"Editor background captured via OCR fallback (confidence {ocrBg.Confidence:F2}).",
                        IsMetadataFallback = false
                    };
                    ocrUsed = true;
                    ocrConfidence = ocrBg.Confidence;
                }
            }

            if (!selected.Success)
            {
                string status = $"{selected.Status} {background.Status}".Trim();
                return CaptureResult.Partial(
                    window,
                    EnvironmentType.IDE,
                    selectedText: string.Empty,
                    backgroundContext: background.Text,
                    selectedMethod: selected.Method,
                    backgroundMethod: background.Method,
                    statusMessage: status);
            }

            bool isPartial = background.IsMetadataFallback;
            string combinedStatus = $"{selected.Status} {background.Status}".Trim();

            return new CaptureResult(
                selectedText:      selected.Text,
                backgroundContext: background.Text,
                windowTitle:       window.Title,
                processName:       window.ProcessName,
                type:              EnvironmentType.IDE,
                selectedMethod:    selected.Method,
                backgroundMethod:  background.Method,
                isPartial:         isPartial,
                isUnsupported:     false,
                statusMessage:     combinedStatus,
                ocrUsed:           ocrUsed,
                ocrConfidence:     ocrConfidence);
        }

        // ─── IDE Embedded Terminal ─────────────────────────────────────────────

        private async Task<CaptureResult> CaptureIdeEmbeddedTerminalAsync(ActiveWindowInfo window)
        {
            CapturePipelines.SelectedCaptureOutcome selected = await CapturePipelines.CaptureEmbeddedTerminalSelectedTextAsync(
                window,
                _compatibilityMode);

            CapturePipelines.BackgroundCaptureOutcome background = CapturePipelines.CaptureTerminalBackground(
                window,
                allowExperimentalDocumentRange: false);

            bool ocrUsed = false;
            float ocrConfidence = 0f;

            // ── OCR selected-text fallback ─────────────────────────────────────
            if (!selected.Success)
            {
                RuntimeLog.Info("IDE", "Embedded terminal: UIA/MSAA/clipboard failed – attempting OCR selected-text fallback.");
                var ocrResult = await OcrCapture.CaptureWithConfidenceAsync(window, OcrCaptureArea.TerminalViewport);

                if (ocrResult.IsUsable(OcrCapture.SelectedTextThreshold))
                {
                    selected = new CapturePipelines.SelectedCaptureOutcome
                    {
                        Text   = ocrResult.Text,
                        Method = CaptureMethod.OcrVisualCapture,
                        Status = $"Embedded terminal text captured via OCR (confidence {ocrResult.Confidence:F2})."
                    };
                    ocrUsed = true;
                    ocrConfidence = ocrResult.Confidence;
                }
                else if (ocrResult.IsUsable(OcrCapture.BackgroundThreshold))
                {
                    if (string.IsNullOrWhiteSpace(background.Text))
                    {
                        background = new CapturePipelines.BackgroundCaptureOutcome
                        {
                            Text   = ocrResult.Text,
                            Method = CaptureMethod.OcrVisualCapture,
                            Status = $"Embedded terminal background via OCR (confidence {ocrResult.Confidence:F2}).",
                            IsMetadataFallback = false
                        };
                    }
                    ocrUsed = true;
                    ocrConfidence = ocrResult.Confidence;
                }
            }

            // ── OCR background fallback ────────────────────────────────────────
            if (!ocrUsed && background.IsMetadataFallback)
            {
                var ocrBg = await OcrCapture.CaptureWithConfidenceAsync(window, OcrCaptureArea.TerminalViewport);
                if (ocrBg.IsUsable(OcrCapture.TerminalThreshold))
                {
                    background = new CapturePipelines.BackgroundCaptureOutcome
                    {
                        Text   = ocrBg.Text,
                        Method = CaptureMethod.OcrVisualCapture,
                        Status = $"Embedded terminal background via OCR (confidence {ocrBg.Confidence:F2}).",
                        IsMetadataFallback = false
                    };
                    ocrUsed = true;
                    ocrConfidence = ocrBg.Confidence;
                }
            }

            bool hasBackground = !string.IsNullOrWhiteSpace(background.Text);
            if (!selected.Success && hasBackground)
            {
                string status = $"{selected.Status} {background.Status}".Trim();
                return CaptureResult.Partial(
                    window,
                    EnvironmentType.IDEEmbeddedTerminal,
                    selectedText: string.Empty,
                    backgroundContext: background.Text,
                    selectedMethod: selected.Method,
                    backgroundMethod: background.Method,
                    statusMessage: status);
            }

            if (!selected.Success)
            {
                string status = $"{selected.Status} {background.Status}".Trim();
                return CaptureResult.Unsupported(
                    window,
                    EnvironmentType.IDEEmbeddedTerminal,
                    selected.Method,
                    background.Method,
                    status,
                    backgroundContext: string.Empty);
            }

            bool isPartial = background.IsMetadataFallback;
            string combinedStatus = $"{selected.Status} {background.Status}".Trim();

            return new CaptureResult(
                selectedText:      selected.Text,
                backgroundContext: background.Text,
                windowTitle:       window.Title,
                processName:       window.ProcessName,
                type:              EnvironmentType.IDEEmbeddedTerminal,
                selectedMethod:    selected.Method,
                backgroundMethod:  background.Method,
                isPartial:         isPartial,
                isUnsupported:     false,
                statusMessage:     combinedStatus,
                ocrUsed:           ocrUsed,
                ocrConfidence:     ocrConfidence);
        }

        // ─── MSAA demotion helper (unchanged) ─────────────────────────────────

        private static bool ShouldDemoteWeakMsaaSelection(
            CapturePipelines.SelectedCaptureOutcome selected,
            CapturePipelines.BackgroundCaptureOutcome background)
        {
            if (selected.Method != CaptureMethod.MsaaSelection || background.Method != CaptureMethod.MsaaContainer)
                return false;

            if (string.IsNullOrWhiteSpace(selected.Text) || string.IsNullOrWhiteSpace(background.Text))
                return false;

            string selectedNormalized   = selected.Text.Trim();
            string backgroundNormalized = background.Text.Trim();
            if (backgroundNormalized.Length == 0) return false;

            if (backgroundNormalized.Contains(selectedNormalized, System.StringComparison.OrdinalIgnoreCase))
                return true;

            int minLength = System.Math.Min(selectedNormalized.Length, backgroundNormalized.Length);
            if (minLength == 0) return false;

            int commonPrefix = 0;
            while (commonPrefix < minLength
                   && char.ToLowerInvariant(selectedNormalized[commonPrefix]) == char.ToLowerInvariant(backgroundNormalized[commonPrefix]))
            {
                commonPrefix++;
            }

            double overlapRatio = (double)commonPrefix / selectedNormalized.Length;
            return overlapRatio >= 0.85;
        }
    }
}
