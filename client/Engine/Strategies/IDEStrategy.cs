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
                allowMsaaFocusedFallback: false,
                allowOcrFallback: false);

            if (selected.Success)
            {
                var snapshot = await EditorBridgeClient.TryCaptureAsync(selected.Text);
                if (snapshot != null)
                    return new CaptureResult(selected.Text, snapshot.BackgroundContext!, window.Title,
                        window.ProcessName, EnvironmentType.IDE, selected.Method, CaptureMethod.EditorBridge,
                        false, false, "Context captured from the active editor buffer, including unsaved changes.");
            }

            CapturePipelines.BackgroundCaptureOutcome background =
                CapturePipelines.CaptureEditorBackground(window, maxChars: 10000, selectedTextHint: selected.Text);

            // ── OCR selected-text fallback ─────────────────────────────────────
            bool ocrUsed = false;
            float ocrConfidence = 0f;

            // OCR is intentionally disabled for selected_text in IDE editor mode.
            // Selected text must come from UIA/MSAA/clipboard compatibility paths only.
            if (!selected.Success)
            {
                RuntimeLog.Warn("IDE", "Selected text capture failed via UIA/MSAA/clipboard. OCR selected-text fallback is disabled by policy.");
            }

            // ── Tier 4: OCR last resort ──────────────────────────────────────────
            // Fires only when all UIA, MSAA, and clipboard compat paths have failed.
            // Uses EditorViewport crop to avoid capturing sidebar/panel chrome.
            // This covers edge cases: unusual VS Code themes, GPU-rendered editors,
            // accessibility completely disabled, or Ctrl+L not bound in the user's config.
            if (background.IsMetadataFallback)
            {
                RuntimeLog.Warn("IDE", "All structured background capture paths failed. Attempting OCR last resort on editor viewport.");
                var ocrResult = await OcrCapture.CaptureWithConfidenceAsync(window, OcrCaptureArea.EditorViewport);
                if (ocrResult.IsUsable(OcrCapture.BackgroundThreshold)
                    && ContextTextWindow.AddsContext(ocrResult.Text, selected.Text))
                {
                    RuntimeLog.Info("IDE", $"OCR last resort succeeded for {window.ProcessName} (confidence {ocrResult.Confidence:F2}, chars {ocrResult.Text.Length}).");
                    background = new CapturePipelines.BackgroundCaptureOutcome
                    {
                        Text = ocrResult.Text,
                        Method = CaptureMethod.OcrVisualCapture,
                        Status = $"IDE editor background captured via OCR last resort (confidence {ocrResult.Confidence:F2}).",
                        IsMetadataFallback = false
                    };
                    ocrUsed = true;
                    ocrConfidence = ocrResult.Confidence;
                }
                else
                {
                    RuntimeLog.Warn("IDE", $"OCR last resort returned insufficient confidence ({ocrResult.Confidence:F2}). Background will be empty.");
                    background = new CapturePipelines.BackgroundCaptureOutcome
                    {
                        Text = string.Empty,
                        Method = CaptureMethod.Unsupported,
                        Status = "IDE editor background unavailable from all structured and OCR paths.",
                        IsMetadataFallback = true
                    };
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

            bool isPartial = background.IsMetadataFallback || ocrUsed;
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

            // ── OCR background fallback ────────────────────────────────────────
            if (!ocrUsed && background.IsMetadataFallback)
            {
                var ocrBg = await OcrCapture.CaptureWithConfidenceAsync(window, OcrCaptureArea.TerminalViewport);
                if (ocrBg.IsUsable(OcrCapture.TerminalThreshold)
                    && ContextTextWindow.AddsContext(ocrBg.Text, selected.Text))
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

            bool isPartial = background.IsMetadataFallback || ocrUsed;
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

        // NOTE:
        // IDE editor OCR helpers were removed intentionally.
        // OCR remains enabled for terminal strategies only.
    }
}
