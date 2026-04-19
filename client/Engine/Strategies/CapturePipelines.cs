using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CodeExplainer.Engine.Models;

namespace CodeExplainer.Engine.Strategies
{
    internal static class CapturePipelines
    {
        internal sealed class SelectedCaptureOutcome
        {
            public string Text { get; init; } = string.Empty;
            public CaptureMethod Method { get; init; } = CaptureMethod.Unsupported;
            public string Status { get; init; } = string.Empty;
            public bool Success => !string.IsNullOrWhiteSpace(Text);
        }

        internal sealed class BackgroundCaptureOutcome
        {
            public string Text { get; init; } = string.Empty;
            public CaptureMethod Method { get; init; } = CaptureMethod.WindowMetadata;
            public bool IsMetadataFallback { get; init; }
            public string Status { get; init; } = string.Empty;
        }

        private sealed class BackgroundCandidate
        {
            public string Text { get; init; } = string.Empty;
            public CaptureMethod Method { get; init; } = CaptureMethod.WindowMetadata;
            public string Status { get; init; } = string.Empty;
            public float Score { get; init; }
            public string SourceLabel { get; init; } = string.Empty;
        }

        public static async Task<SelectedCaptureOutcome> CaptureSelectedTextAsync(
            ActiveWindowInfo window,
            ClipboardCompatibilityMode compatibilityMode,
            bool preferMsaaFirst,
            bool allowMsaaFocusedFallback = true,
            bool allowOcrFallback = true)
        {
            bool isGoogleDocs = window.Title.IndexOf("Google Docs", System.StringComparison.OrdinalIgnoreCase) >= 0;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (isGoogleDocs)
                {
                    RuntimeLog.Warn("CapturePipeline", "Skipping UIA/MSAA selected-text grab for Google Docs; relying purely on clipboard compatibility.");
                    goto skipMsaaFirstUiaSelection;
                }
                if (!preferMsaaFirst && UiAutomationCapture.TryGetSelectedText(5000, out string uiaSelected))
                {
                    if (IsRejectedIdeSelectedSource(window, uiaSelected))
                    {
                        RuntimeLog.Warn("CapturePipeline", $"Rejected UIA selected source for {window.ProcessName}.");
                    }
                    else
                    {
                        string cleaned = CleanKnownUiNoise(window, uiaSelected, 5000);
                        if (LooksLikeSelectedUiNoise(window, cleaned))
                        {
                            RuntimeLog.Warn("CapturePipeline", $"Rejected noisy UIA selected text for {window.ProcessName}.");
                            goto skipUiaSelection;
                        }

                        return new SelectedCaptureOutcome
                        {
                            Text = cleaned,
                            Method = CaptureMethod.UiaTextPatternSelection,
                            Status = "Selected text captured via UI Automation selection."
                        };
                    }
                }
            skipUiaSelection:

                bool hasMsaaSelectionSignal = MsaaCapture.HasSelection(window.Hwnd);
                if (MsaaCapture.TryGetExplicitSelectionText(window.Hwnd, 5000, out string msaaSelected))
                {
                    if (!hasMsaaSelectionSignal)
                    {
                        RuntimeLog.Warn("CapturePipeline", $"Rejected MSAA selected text for {window.ProcessName}: no explicit selection signal.");
                    }
                    else if (IsRejectedIdeSelectedSource(window, msaaSelected))
                    {
                        RuntimeLog.Warn("CapturePipeline", $"Rejected MSAA selected source for {window.ProcessName}.");
                    }
                    else
                    {
                        string cleaned = CleanKnownUiNoise(window, msaaSelected, 5000);
                        if (LooksLikeSelectedUiNoise(window, cleaned))
                        {
                            RuntimeLog.Warn("CapturePipeline", $"Rejected noisy MSAA selected text for {window.ProcessName}.");
                            goto skipMsaaSelection;
                        }

                        RuntimeLog.Info("CapturePipeline", $"MSAA selected text used for {window.ProcessName}.");
                        return new SelectedCaptureOutcome
                        {
                            Text = cleaned,
                            Method = CaptureMethod.MsaaSelection,
                            Status = "Selected text captured via MSAA."
                        };
                    }
                }
            skipMsaaSelection:

                if (allowMsaaFocusedFallback && MsaaCapture.TryGetFocusedText(window.Hwnd, 5000, out string msaaFocused))
                {
                    if (IsRejectedIdeSelectedSource(window, msaaFocused))
                    {
                        RuntimeLog.Warn("CapturePipeline", $"Rejected MSAA focused source for {window.ProcessName}.");
                    }
                    else
                    {
                        string cleaned = CleanKnownUiNoise(window, msaaFocused, 5000);
                        if (LooksLikeSelectedUiNoise(window, cleaned))
                        {
                            RuntimeLog.Warn("CapturePipeline", $"Rejected noisy MSAA focused text for {window.ProcessName}.");
                        }
                        else
                        {
                            RuntimeLog.Info("CapturePipeline", $"MSAA focused-text selected capture used for {window.ProcessName}.");
                            return new SelectedCaptureOutcome
                            {
                                Text = cleaned,
                                Method = CaptureMethod.MsaaFocusedText,
                                Status = "Selected text captured via MSAA focused-text route."
                            };
                        }
                    }
                }

                if (preferMsaaFirst && UiAutomationCapture.TryGetSelectedText(5000, out uiaSelected))
                {
                    if (IsRejectedIdeSelectedSource(window, uiaSelected))
                    {
                        RuntimeLog.Warn("CapturePipeline", $"Rejected UIA selected source for {window.ProcessName}.");
                    }
                    else
                    {
                        string cleaned = CleanKnownUiNoise(window, uiaSelected, 5000);
                        if (LooksLikeSelectedUiNoise(window, cleaned))
                        {
                            RuntimeLog.Warn("CapturePipeline", $"Rejected noisy UIA selected text for {window.ProcessName}.");
                            goto skipMsaaFirstUiaSelection;
                        }

                        return new SelectedCaptureOutcome
                        {
                            Text = cleaned,
                            Method = CaptureMethod.UiaTextPatternSelection,
                            Status = "Selected text captured via UI Automation selection."
                        };
                    }
                }
            skipMsaaFirstUiaSelection:

                if (attempt == 0)
                {
                    await Task.Delay(80);
                }
            }

            bool hasSelectionSignal = false;
            if (compatibilityMode.Enabled)
            {
                hasSelectionSignal = UiAutomationCapture.HasSelection() || MsaaCapture.HasSelection(window.Hwnd);
            }

            bool allowCompatWithoutSignal = compatibilityMode.Enabled
                && (
                    ClipboardCompatibilityMode.IsIdeProcess(window.ProcessName) 
                    || isGoogleDocs
                );

            if (compatibilityMode.Enabled && !hasSelectionSignal && !allowCompatWithoutSignal)
            {
                RuntimeLog.Info("CompatClipboard", $"Skipped for {window.ProcessName}: no existing selection signal detected.");
            }
            else if (compatibilityMode.Enabled && !hasSelectionSignal && allowCompatWithoutSignal)
            {
                RuntimeLog.Warn("CompatClipboard", $"No UIA/MSAA selection signal for {window.ProcessName}; attempting compatibility mode due to known IDE/Browser accessibility gaps.");
            }

            string? compatibilityText = null;
            if (compatibilityMode.Enabled && (hasSelectionSignal || allowCompatWithoutSignal))
            {
                compatibilityText = await compatibilityMode.TryCaptureSelectedTextAsync(window);
            }

            if (!string.IsNullOrWhiteSpace(compatibilityText))
            {
                if (IsRejectedIdeSelectedSource(window, compatibilityText!))
                {
                    RuntimeLog.Warn("CapturePipeline", $"Rejected compatibility selected source for {window.ProcessName}.");
                }
                else
                {
                    string cleanedCompatibility = CleanKnownUiNoise(window, compatibilityText!, 5000);
                    if (LooksLikeSelectedUiNoise(window, cleanedCompatibility))
                    {
                        RuntimeLog.Warn("CapturePipeline", $"Rejected noisy compatibility-mode selected text for {window.ProcessName}.");
                    }
                    else
                    {
                        RuntimeLog.Warn("CapturePipeline", $"Compatibility mode selected text used for {window.ProcessName}.");
                        return new SelectedCaptureOutcome
                        {
                            Text = cleanedCompatibility,
                            Method = CaptureMethod.ClipboardCompatibility,
                            Status = hasSelectionSignal
                                ? "Selected text captured via optional compatibility mode."
                                : "Selected text captured via optional compatibility mode without UIA/MSAA selection signal."
                        };
                    }
                }
            }

            string compatibilityStatus = compatibilityMode.Enabled
                ? hasSelectionSignal
                    ? "Optional compatibility mode did not capture selected text."
                    : allowCompatWithoutSignal
                        ? "Optional compatibility mode was attempted without UIA/MSAA selection signal but did not capture usable selected text."
                        : "Optional compatibility mode was skipped because no selected text signal was detected."
                : "Optional compatibility mode is disabled (set CODE_EXPLAINER_CLIPBOARD_COMPAT=1 to enable selected-text compatibility).";

            string environmentHint = IsVsCodeLike(window.ProcessName)
                ? " VS Code/Cursor may require accessibility support set to On or Auto for non-clipboard selection capture."
                : string.Empty;

            // OCR fallback – attempt when UIA/MSAA/clipboard all failed (optional per strategy)
            if (allowOcrFallback)
            {
                var ocrText = await OcrCapture.CaptureAsync(window);
                if (!string.IsNullOrWhiteSpace(ocrText))
                {
                    RuntimeLog.Info("CapturePipeline", $"OCR fallback succeeded for {window.ProcessName}");
                    return new SelectedCaptureOutcome
                    {
                        Text = ocrText,
                        Method = CaptureMethod.OcrVisualCapture,
                        Status = "Background text captured via native OCR fallback."
                    };
                }
            }

            // If OCR also fails, return unsupported outcome
            return new SelectedCaptureOutcome
            {
                Method = CaptureMethod.Unsupported,
                Status = $"Selected text could not be captured via UIA or MSAA. {compatibilityStatus}{environmentHint}"
            };
        }

        public static async Task<SelectedCaptureOutcome> CaptureEmbeddedTerminalSelectedTextAsync(
            ActiveWindowInfo window,
            ClipboardCompatibilityMode compatibilityMode)
        {
            if (UiAutomationCapture.TryGetSelectedText(5000, out string uiaSelected))
            {
                if (IsRejectedIdeSelectedSource(window, uiaSelected))
                {
                    RuntimeLog.Warn("CapturePipeline", $"Rejected embedded-terminal UIA selected source for {window.ProcessName}.");
                }
                else
                {
                    string cleaned = CleanKnownUiNoise(window, uiaSelected, 5000);
                    if (!string.IsNullOrWhiteSpace(cleaned) && !LooksLikeSelectedUiNoise(window, cleaned))
                    {
                        return new SelectedCaptureOutcome
                        {
                            Text = cleaned,
                            Method = CaptureMethod.UiaTextPatternSelection,
                            Status = "Selected terminal text captured via UI Automation selection."
                        };
                    }
                }
            }

            if (MsaaCapture.TryGetExplicitSelectionText(window.Hwnd, 5000, out string msaaSelected))
            {
                if (IsRejectedIdeSelectedSource(window, msaaSelected))
                {
                    RuntimeLog.Warn("CapturePipeline", $"Rejected embedded-terminal MSAA selected source for {window.ProcessName}.");
                }
                else
                {
                    string cleaned = CleanKnownUiNoise(window, msaaSelected, 5000);
                    if (!string.IsNullOrWhiteSpace(cleaned) && !LooksLikeSelectedUiNoise(window, cleaned))
                    {
                        return new SelectedCaptureOutcome
                        {
                            Text = cleaned,
                            Method = CaptureMethod.MsaaSelection,
                            Status = "Selected terminal text captured via MSAA."
                        };
                    }
                }
            }

            string? compatibilityText = await compatibilityMode.TryCaptureSelectedTerminalTextAsync(window);
            if (!string.IsNullOrWhiteSpace(compatibilityText))
            {
                if (IsRejectedIdeSelectedSource(window, compatibilityText))
                {
                    RuntimeLog.Warn("CapturePipeline", $"Rejected embedded-terminal compatibility selected source for {window.ProcessName}.");
                }
                else
                {
                    string cleaned = CleanKnownUiNoise(window, compatibilityText, 5000);
                    if (!string.IsNullOrWhiteSpace(cleaned) && !LooksLikeSelectedUiNoise(window, cleaned))
                    {
                        return new SelectedCaptureOutcome
                        {
                            Text = cleaned,
                            Method = CaptureMethod.ClipboardCompatTerminal,
                            Status = "Selected terminal text captured via clipboard compatibility mode."
                        };
                    }
                }
            }

            string compatibilityStatus = "Terminal clipboard compatibility mode did not capture selected text.";

            return new SelectedCaptureOutcome
            {
                Method = CaptureMethod.Unsupported,
                Status = $"Selected text could not be captured via UIA or MSAA. {compatibilityStatus}"
            };
        }

        public static BackgroundCaptureOutcome CaptureEditorBackground(
            ActiveWindowInfo window,
            int maxChars = 10000,
            string? selectedTextHint = null)
        {
            bool isVsCodeLike = IsVsCodeLike(window.ProcessName);
            var candidates = new List<BackgroundCandidate>();

            // ── Tier 1A: Selection-anchored neighbor lines (HIGHEST PRIORITY for all IDEs)
            // This is anchored to the cursor position and immune to sidebar/panel capture.
            TryAddEditorBackgroundCandidate(
                candidates,
                window,
                (int limit, out string text) => UiAutomationCapture.TryGetNeighborLinesViaTextRange(limit, 14, 14, out text),
                maxChars,
                selectedTextHint,
                CaptureMethod.IdeContextTextRange,
                "Background context captured via UIA selection-anchored neighbor lines.");

            // ── Tier 1B: Document range from focused element
            TryAddEditorBackgroundCandidate(
                candidates,
                window,
                UiAutomationCapture.TryGetDocumentRangeText,
                maxChars,
                selectedTextHint,
                CaptureMethod.UiaTextPatternDocumentRange,
                "Background context captured via UIA document range.");

            if (isVsCodeLike)
            {
                // ── Tier 1C: Document range via descendant walk (VS Code specific)
                TryAddEditorBackgroundCandidate(
                    candidates,
                    window,
                    UiAutomationCapture.TryGetDocumentRangeTextFromDescendants,
                    maxChars,
                    selectedTextHint,
                    CaptureMethod.UiaTextPatternDocumentRange,
                    "Background context captured via descendant UIA document range.");

                // ── Tier 1D: Visible ranges (can capture sidebar — filtered by sidebar detector)
                TryAddEditorBackgroundCandidate(
                    candidates,
                    window,
                    UiAutomationCapture.TryGetVisibleRangesText,
                    maxChars,
                    selectedTextHint,
                    CaptureMethod.UiaTextPatternVisibleRanges,
                    "Background context captured via UIA visible ranges.");

                TryAddEditorBackgroundCandidate(
                    candidates,
                    window,
                    UiAutomationCapture.TryGetVisibleRangesTextFromDescendants,
                    maxChars,
                    selectedTextHint,
                    CaptureMethod.UiaTextPatternVisibleRanges,
                    "Background context captured via descendant UIA visible ranges.");
            }
            else
            {
                // Non-VS Code IDEs: visible ranges are usually safe (no sidebar tree mixing)
                TryAddEditorBackgroundCandidate(
                    candidates,
                    window,
                    UiAutomationCapture.TryGetVisibleRangesText,
                    maxChars,
                    selectedTextHint,
                    CaptureMethod.UiaTextPatternVisibleRanges,
                    "Background context captured via UIA visible ranges.");
            }

            // ── Tier 2: Container walks
            TryAddEditorBackgroundCandidate(
                candidates,
                window,
                UiAutomationCapture.TryGetNearestContainerText,
                maxChars,
                selectedTextHint,
                CaptureMethod.UiaTreeContainer,
                "Background context captured via UIA editor-local container.");

            TryAddEditorBackgroundCandidate(
                candidates,
                window,
                (int limit, out string text) => MsaaCapture.TryGetContainerText(window.Hwnd, limit, out text),
                maxChars,
                selectedTextHint,
                CaptureMethod.MsaaContainer,
                "Background context captured via MSAA container route.");

            BackgroundCandidate? bestCandidate = GetBestBackgroundCandidate(candidates);
            if (bestCandidate != null)
            {
                RuntimeLog.Info("CapturePipeline", $"{bestCandidate.SourceLabel} background context selected for {window.ProcessName} (score {bestCandidate.Score:F2}).");
                return new BackgroundCaptureOutcome
                {
                    Text = bestCandidate.Text,
                    Method = bestCandidate.Method,
                    Status = bestCandidate.Status
                };
            }

            return MetadataFallback(window, "Background context unavailable from editor surface; using window metadata.");
        }

        public static BackgroundCaptureOutcome CaptureTerminalBackground(ActiveWindowInfo window, bool allowExperimentalDocumentRange = false)
        {
            if (UiAutomationCapture.TryGetVisibleRangesText(6000, out string visibleText))
            {
                return new BackgroundCaptureOutcome
                {
                    Text = visibleText,
                    Method = CaptureMethod.UiaTextPatternVisibleRanges,
                    Status = "Terminal background captured via UIA visible ranges."
                };
            }

            if (allowExperimentalDocumentRange && UiAutomationCapture.TryGetDocumentRangeText(6000, out string experimentalText))
            {
                RuntimeLog.Warn("CapturePipeline", "Using experimental UIA document-range terminal context.");
                return new BackgroundCaptureOutcome
                {
                    Text = experimentalText,
                    Method = CaptureMethod.UiaTextPatternDocumentRange,
                    Status = "Terminal background captured using experimental UIA document range."
                };
            }

            if (MsaaCapture.TryGetContainerText(window.Hwnd, 6000, out string msaaText))
            {
                RuntimeLog.Info("CapturePipeline", $"MSAA terminal context used for {window.ProcessName}.");
                return new BackgroundCaptureOutcome
                {
                    Text = msaaText,
                    Method = CaptureMethod.MsaaContainer,
                    Status = "Terminal background captured via MSAA container route."
                };
            }

            return MetadataFallback(window, "Terminal background context unavailable; using window metadata.");
        }

        public static async System.Threading.Tasks.Task<BackgroundCaptureOutcome> CaptureBrowserContainerBackground(ActiveWindowInfo window, int maxChars = 3000)
        {
            // Minimum threshold: UIA/MSAA returning < 30 chars typically means the container
            // only echoed the selected word back (common in canvas-based apps like Google Docs).
            const int MinUsableBackground = 30;

            if (UiAutomationCapture.TryGetNearestContainerText(maxChars, out string containerText)
                && !string.IsNullOrWhiteSpace(containerText)
                && containerText.Length >= MinUsableBackground)
            {
                return new BackgroundCaptureOutcome
                {
                    Text = containerText,
                    Method = CaptureMethod.UiaTreeContainer,
                    Status = "Browser background captured from nearest UIA container."
                };
            }

            if (MsaaCapture.TryGetContainerText(window.Hwnd, maxChars, out string msaaText)
                && !string.IsNullOrWhiteSpace(msaaText)
                && msaaText.Length >= MinUsableBackground)
            {
                RuntimeLog.Info("CapturePipeline", $"MSAA browser context used for {window.ProcessName}.");
                return new BackgroundCaptureOutcome
                {
                    Text = msaaText,
                    Method = CaptureMethod.MsaaContainer,
                    Status = "Browser background captured via MSAA container route."
                };
            }

            // OCR fallback – for canvas-based apps like Google Docs where UIA/MSAA can't read the DOM
            RuntimeLog.Info("CapturePipeline", $"UIA/MSAA browser background insufficient for {window.ProcessName}; attempting OCR fallback.");
            var ocrText = await OcrCapture.CaptureAsync(window);
            if (!string.IsNullOrWhiteSpace(ocrText))
            {
                RuntimeLog.Info("CapturePipeline", $"OCR background succeeded for {window.ProcessName} ({ocrText.Length} chars).");
                return new BackgroundCaptureOutcome
                {
                    Text = ocrText,
                    Method = CaptureMethod.OcrVisualCapture,
                    Status = "Browser background captured via native OCR (canvas-based app detected)."
                };
            }

            return MetadataFallback(window, "Browser background context unavailable; using window metadata.");
        }


        public static BackgroundCaptureOutcome CaptureFirefoxBackground(ActiveWindowInfo window, int maxChars = 3000)
        {
            if (MsaaCapture.TryGetContainerText(window.Hwnd, maxChars, out string msaaText))
            {
                RuntimeLog.Info("CapturePipeline", $"Firefox background captured via MSAA for {window.ProcessName}.");
                return new BackgroundCaptureOutcome
                {
                    Text = msaaText,
                    Method = CaptureMethod.MsaaContainer,
                    Status = "Firefox background captured via MSAA container route."
                };
            }

            return MetadataFallback(window, "Firefox background context unavailable; using window metadata.");
        }

        public static BackgroundCaptureOutcome CaptureUnknownBackground(ActiveWindowInfo window, int maxChars = 4000)
        {
            if (UiAutomationCapture.TryGetVisibleRangesText(maxChars, out string visibleText))
            {
                return new BackgroundCaptureOutcome
                {
                    Text = visibleText,
                    Method = CaptureMethod.UiaTextPatternVisibleRanges,
                    Status = "Background context captured via UIA visible ranges."
                };
            }

            if (UiAutomationCapture.TryGetNearestContainerText(maxChars, out string containerText))
            {
                return new BackgroundCaptureOutcome
                {
                    Text = containerText,
                    Method = CaptureMethod.UiaTreeContainer,
                    Status = "Background context captured via UIA container walk."
                };
            }

            if (MsaaCapture.TryGetContainerText(window.Hwnd, maxChars, out string msaaText))
            {
                RuntimeLog.Info("CapturePipeline", $"MSAA unknown-app context used for {window.ProcessName}.");
                return new BackgroundCaptureOutcome
                {
                    Text = msaaText,
                    Method = CaptureMethod.MsaaContainer,
                    Status = "Background context captured via MSAA container route."
                };
            }

            return MetadataFallback(window, "Background context unavailable; using window metadata.");
        }

        public static BackgroundCaptureOutcome MetadataFallback(ActiveWindowInfo window, string status)
        {
            return new BackgroundCaptureOutcome
            {
                Text = CaptureResult.BuildMetadataContext(window),
                Method = CaptureMethod.WindowMetadata,
                IsMetadataFallback = true,
                Status = status
            };
        }

        private static bool LooksLikeSelectedUiNoise(ActiveWindowInfo window, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            string normalized = text.Trim();
            if (normalized.StartsWith(window.Title, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IsVsCodeLike(window.ProcessName) && LooksLikePlainVsCodeNotification(normalized))
            {
                return true;
            }

            return ContainsKnownUiNoise(normalized) || LooksLikeIdeChromeLabelsOnly(normalized);
        }

        private static bool LooksLikeEditorBackgroundNoise(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            string normalized = text.Trim();
            return ContainsKnownUiNoise(normalized)
                || normalized.IndexOf("editor is not accessible", System.StringComparison.OrdinalIgnoreCase) >= 0
                || LooksLikeIdeChromeLabelsOnly(normalized);
        }

        private static bool IsRejectedIdeSelectedSource(ActiveWindowInfo window, string rawText)
        {
            if (!IsVsCodeLike(window.ProcessName))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(rawText))
            {
                return true;
            }

            string raw = rawText.Trim();
            if (ContainsKnownUiNoise(raw) && !HasCodeSignal(raw))
            {
                return true;
            }

            return LooksLikeIdeChromeLabelsOnly(raw);
        }

        private static bool IsRejectedIdeBackgroundSource(ActiveWindowInfo window, string rawText)
        {
            if (!IsVsCodeLike(window.ProcessName))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(rawText))
            {
                return true;
            }

            string raw = rawText.Trim();
            if (ContainsKnownUiNoise(raw) && !HasCodeSignal(raw))
            {
                return true;
            }

            return LooksLikeIdeChromeLabelsOnly(raw);
        }

        private static bool LooksLikePlausibleEditorBackground(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string normalized = text.Trim();
            if (LooksLikeIdeChromeLabelsOnly(normalized))
            {
                return false;
            }

            int lineCount = normalized.Split('\n').Length;
            if (normalized.Length < 30 && lineCount < 2)
            {
                return false;
            }

            if (HasCodeSignal(normalized) && (normalized.Length >= 40 || lineCount >= 2))
            {
                return true;
            }

            return lineCount >= 2 && normalized.Length >= 30;
        }

        private delegate bool BackgroundTextSource(int maxChars, out string text);

        private static void TryAddEditorBackgroundCandidate(
            List<BackgroundCandidate> candidates,
            ActiveWindowInfo window,
            BackgroundTextSource source,
            int maxChars,
            string? selectedTextHint,
            CaptureMethod method,
            string status)
        {
            if (!source(maxChars, out string rawText))
            {
                return;
            }

            string sourceLabel = method switch
            {
                CaptureMethod.UiaTextPatternDocumentRange => "document-range",
                CaptureMethod.UiaTextPatternVisibleRanges => "visible-ranges",
                CaptureMethod.UiaTreeContainer => "container",
                CaptureMethod.IdeContextTextRange => "anchored-text-range",
                CaptureMethod.MsaaContainer => "msaa-container",
                _ => method.ToApiValue()
            };

            // ── FIXED: Clean noise FIRST, then reject on the cleaned text.
            // Previously IsRejectedIdeBackgroundSource ran on raw dirty text, discarding
            // real code that happened to share a blob with a noise phrase.
            string cleaned = CleanKnownUiNoise(window, rawText, maxChars);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                RuntimeLog.Warn("CapturePipeline", $"Rejected {sourceLabel} background for {window.ProcessName}: empty after noise cleaning.");
                return;
            }

            if (IsRejectedIdeBackgroundSource(window, cleaned))
            {
                RuntimeLog.Warn("CapturePipeline", $"Rejected {sourceLabel} background for {window.ProcessName}: cleaned text still looks like IDE chrome or UI noise.");
                return;
            }

            // ── Sidebar content detector: reject VS Code file-tree dumps
            if (LooksLikeSidebarContent(cleaned))
            {
                RuntimeLog.Warn("CapturePipeline", $"Rejected {sourceLabel} background for {window.ProcessName}: candidate looks like sidebar/file-tree content.");
                return;
            }

            cleaned = RefineEditorBackgroundCandidate(cleaned, selectedTextHint, maxChars);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                RuntimeLog.Warn("CapturePipeline", $"Rejected refined {sourceLabel} background for {window.ProcessName}: nothing usable remained after refining.");
                return;
            }

            if (LooksLikeEditorBackgroundNoise(cleaned))
            {
                RuntimeLog.Warn("CapturePipeline", $"Rejected {sourceLabel} background for {window.ProcessName}: still looks like editor chrome after refining.");
                return;
            }

            bool anchored = LooksLikeSelectionAnchoredBackground(cleaned, selectedTextHint);
            if (!anchored && !LooksLikePlausibleEditorBackground(cleaned))
            {
                RuntimeLog.Warn("CapturePipeline", $"Rejected {sourceLabel} background for {window.ProcessName}: candidate did not look like plausible editor content.");
                return;
            }

            if (!anchored && !AddsUsefulBackgroundContext(cleaned, selectedTextHint))
            {
                RuntimeLog.Warn("CapturePipeline", $"Rejected {sourceLabel} background for {window.ProcessName}: candidate did not add useful context beyond the selection.");
                return;
            }

            // ── FIXED: Lowered threshold from 0.40 → 0.20 (upstream gates are the real guard)
            float score = ScoreBackgroundCandidate(cleaned, selectedTextHint);
            if (score < 0.20f)
            {
                RuntimeLog.Warn("CapturePipeline", $"Rejected {sourceLabel} background for {window.ProcessName}: score {score:F2} below threshold.");
                return;
            }

            candidates.Add(new BackgroundCandidate
            {
                Text = cleaned,
                Method = method,
                Status = status,
                Score = score,
                SourceLabel = sourceLabel
            });
        }

        private static BackgroundCandidate? GetBestBackgroundCandidate(List<BackgroundCandidate> candidates)
        {
            BackgroundCandidate? best = null;
            foreach (BackgroundCandidate candidate in candidates)
            {
                if (best == null || candidate.Score > best.Score)
                {
                    best = candidate;
                }
            }

            return best;
        }

        private static float ScoreBackgroundCandidate(string text, string? selectedTextHint)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0f;
            }

            string normalized = text.Trim();
            int lineCount = normalized.Split('\n').Length;
            float score = 0f;

            if (lineCount >= 5)
            {
                score += 0.25f;
            }
            else if (lineCount >= 2)
            {
                score += 0.15f;
            }

            if (HasCodeSignal(normalized))
            {
                score += 0.30f;
            }

            if (HasIndentationSignal(normalized))
            {
                score += 0.15f;
            }

            if (!LooksLikeIdeChromeLabelsOnly(normalized))
            {
                score += 0.20f;
            }

            if (normalized.Length >= 200)
            {
                score += 0.10f;
            }
            else if (normalized.Length >= 80)
            {
                score += 0.05f;
            }

            if (LooksLikeSelectionAnchoredBackground(normalized, selectedTextHint))
            {
                score += 0.10f;
            }

            return System.Math.Min(score, 1f);
        }

        private static bool LooksLikeSelectionAnchoredBackground(string text, string? selectedTextHint)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(selectedTextHint))
            {
                return false;
            }

            string candidate = text.Trim();
            string selected = selectedTextHint.Trim();
            if (candidate.Length < 40 || candidate.Length <= selected.Length)
            {
                return false;
            }

            if (candidate.IndexOf(selected, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return candidate.Split('\n').Length >= 2 || candidate.Length >= selected.Length + 25;
            }

            string[] selectedTokens = ExtractContextTokens(selected);
            if (selectedTokens.Length == 0)
            {
                return false;
            }

            int hits = 0;
            foreach (string token in selectedTokens)
            {
                if (candidate.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hits++;
                }
            }

            return hits >= System.Math.Min(2, selectedTokens.Length)
                && (candidate.Split('\n').Length >= 2 || candidate.Length >= selected.Length + 25);
        }

        internal static bool AddsUsefulBackgroundContext(string text, string? selectedTextHint)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(selectedTextHint))
            {
                return true;
            }

            string candidate = text.Trim();
            string selected = selectedTextHint.Trim();
            if (candidate.Length == 0)
            {
                return false;
            }

            if (string.Equals(candidate, selected, System.StringComparison.Ordinal))
            {
                return false;
            }

            string collapsedCandidate = CollapseWhitespace(candidate);
            string collapsedSelected = CollapseWhitespace(selected);
            if (string.Equals(collapsedCandidate, collapsedSelected, System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if ((collapsedCandidate.Contains(collapsedSelected, System.StringComparison.OrdinalIgnoreCase)
                    || collapsedSelected.Contains(collapsedCandidate, System.StringComparison.OrdinalIgnoreCase))
                && System.Math.Abs(collapsedCandidate.Length - collapsedSelected.Length) < 24)
            {
                return false;
            }

            int candidateLines = candidate.Split('\n').Length;
            int distinctContextLines = CountDistinctContextLines(candidate, selected);
            if (distinctContextLines == 0)
            {
                return false;
            }

            if (candidateLines >= 2)
            {
                return true;
            }

            if (candidate.Length <= selected.Length + 12)
            {
                return false;
            }

            if (candidate.StartsWith(selected, System.StringComparison.OrdinalIgnoreCase)
                || candidate.EndsWith(selected, System.StringComparison.OrdinalIgnoreCase)
                || candidate.IndexOf(selected, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return candidate.Length >= selected.Length + 20;
            }

            return candidate.Length >= 40;
        }

        internal static string RefineEditorBackgroundForExternalUse(string text, string? selectedTextHint, int maxChars)
        {
            return RefineEditorBackgroundCandidate(text, selectedTextHint, maxChars);
        }

        private static string RefineEditorBackgroundCandidate(string text, string? selectedTextHint, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string normalized = text.Replace("\r\n", "\n").Trim();
            string[] rawLines = normalized.Split('\n');
            var keptLines = new System.Collections.Generic.List<string>();
            foreach (string rawLine in rawLines)
            {
                string line = rawLine.TrimEnd();
                if (ShouldDiscardEditorNoiseLine(line))
                {
                    continue;
                }

                keptLines.Add(line);
            }

            if (keptLines.Count == 0)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(selectedTextHint))
            {
                int anchorIndex = FindAnchorLineIndex(keptLines, selectedTextHint!);
                if (anchorIndex >= 0)
                {
                    int selectedLines = System.Math.Max(1, selectedTextHint!.Replace("\r\n", "\n").Split('\n').Length);
                    // FIXED: Expand background context capture window drastically.
                    // Grab up to 60 lines above, and 60 lines down (was 6 and 8).
                    int start = System.Math.Max(0, anchorIndex - 60);
                    int end = System.Math.Min(keptLines.Count - 1, anchorIndex + selectedLines + 60);
                    keptLines = keptLines.GetRange(start, end - start + 1);
                }
            }

            var builder = new StringBuilder();
            foreach (string line in keptLines)
            {
                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(line);
                if (builder.Length >= maxChars)
                {
                    break;
                }
            }

            string output = builder.ToString().Trim();
            if (output.Length > maxChars)
            {
                output = output.Substring(0, maxChars);
            }

            return output;
        }

        private static int FindAnchorLineIndex(System.Collections.Generic.List<string> lines, string selectedTextHint)
        {
            string selected = selectedTextHint.Replace("\r\n", "\n").Trim();
            if (selected.Length == 0)
            {
                return -1;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].IndexOf(selected, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return i;
                }
            }

            string[] tokens = ExtractContextTokens(selected);
            if (tokens.Length == 0)
            {
                return -1;
            }

            int bestIndex = -1;
            int bestHits = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                int hits = 0;
                foreach (string token in tokens)
                {
                    if (lines[i].IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hits++;
                    }
                }

                if (hits > bestHits)
                {
                    bestHits = hits;
                    bestIndex = i;
                }
            }

            return bestHits >= System.Math.Min(2, tokens.Length) ? bestIndex : -1;
        }

        private static int CountDistinctContextLines(string candidate, string selected)
        {
            string[] candidateLines = candidate.Replace("\r\n", "\n").Split('\n');
            string[] selectedLines = selected.Replace("\r\n", "\n").Split('\n');
            var selectedSet = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (string line in selectedLines)
            {
                string normalized = CollapseWhitespace(line);
                if (normalized.Length > 0)
                {
                    selectedSet.Add(normalized);
                }
            }

            int distinct = 0;
            foreach (string line in candidateLines)
            {
                string normalized = CollapseWhitespace(line);
                if (normalized.Length == 0)
                {
                    continue;
                }

                if (!selectedSet.Contains(normalized))
                {
                    distinct++;
                }
            }

            return distinct;
        }

        private static string CollapseWhitespace(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            bool previousWasWhitespace = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!previousWasWhitespace)
                    {
                        builder.Append(' ');
                        previousWasWhitespace = true;
                    }

                    continue;
                }

                builder.Append(c);
                previousWasWhitespace = false;
            }

            return builder.ToString().Trim();
        }

        private static bool ShouldDiscardEditorNoiseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return true;
            }

            string trimmed = line.Trim();
            string lower = trimmed.ToLowerInvariant();

            if (ContainsKnownUiNoise(trimmed))
            {
                return true;
            }

            string[] exactLabels =
            {
                "explorer",
                "search",
                "source control",
                "run and debug",
                "extensions",
                "outline",
                "problems",
                "output",
                "debug console",
                "terminal",
                "ports",
                "welcome",
                "application menu",
                "open codex sidebar",
                "c# project details",
                "inline bookmarks"
            };

            foreach (string label in exactLabels)
            {
                if (lower.Equals(label, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            int letterOrDigitCount = 0;
            int punctuationCount = 0;
            foreach (char c in trimmed)
            {
                if (char.IsLetterOrDigit(c))
                {
                    letterOrDigitCount++;
                }
                else if ("{}[]();.,:=<>\"'/_".IndexOf(c) >= 0)
                {
                    punctuationCount++;
                }
            }

            if (letterOrDigitCount <= 1 && punctuationCount == 0)
            {
                return true;
            }

            if (trimmed.Length < 24 && !HasCodeSignal(trimmed) && punctuationCount == 0 && letterOrDigitCount < 6)
            {
                return true;
            }

            if ((lower.EndsWith(".cs", System.StringComparison.Ordinal)
                    || lower.EndsWith(".json", System.StringComparison.Ordinal)
                    || lower.EndsWith(".csproj", System.StringComparison.Ordinal))
                && !HasCodeSignal(trimmed)
                && trimmed.Length < 48)
            {
                return true;
            }

            return false;
        }

        private static string[] ExtractContextTokens(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return System.Array.Empty<string>();
            }

            var tokens = new System.Collections.Generic.List<string>();
            foreach (string raw in text.Split(new[] { ' ', '\t', '\r', '\n', '(', ')', '{', '}', '[', ']', ';', ',', '.', ':', '=', '+', '-', '*', '/', '<', '>', '"' , '\'' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                string token = raw.Trim();
                if (token.Length < 3)
                {
                    continue;
                }

                bool isAllDigits = true;
                foreach (char c in token)
                {
                    if (!char.IsDigit(c))
                    {
                        isAllDigits = false;
                        break;
                    }
                }

                if (isAllDigits)
                {
                    continue;
                }

                if (!tokens.Contains(token))
                {
                    tokens.Add(token);
                    if (tokens.Count >= 6)
                    {
                        break;
                    }
                }
            }

            return tokens.ToArray();
        }

        private static bool ContainsKnownUiNoise(string normalizedText)
        {
            return normalizedText.IndexOf("vscode-file://", System.StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedText.IndexOf("vscode-app/", System.StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedText.IndexOf("vscode-webview://", System.StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedText.IndexOf("The editor is not accessible", System.StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedText.IndexOf("editor is not accessible", System.StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedText.IndexOf("screen reader optimized mode", System.StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedText.IndexOf("project system ran into an unexpected problem", System.StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedText.IndexOf("report it to the product team", System.StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedText.IndexOf("report it to the project system", System.StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedText.IndexOf("ProjectSystemServerFault_", System.StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedText.IndexOf("ms-dotnettools.csdevkit", System.StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedText.IndexOf("WindowTitle:", System.StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedText.IndexOf("ProcessName:", System.StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedText.IndexOf("screen reader in Google Docs", System.StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedText.IndexOf("screen reader support", System.StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedText.IndexOf("Braille support is", System.StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedText.IndexOf("language server crashed", System.StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedText.IndexOf("source: C/C++, notification", System.StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedText.IndexOf("notification, Inspect the response", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Detects VS Code sidebar / file-explorer tree dumps that UIA visible-ranges
        /// occasionally returns instead of editor code. These look like:
        ///   JobApi\nControllers\nModels\nProperties\n0 references\nINLINE BOOKMARKS...
        /// They contain code-like tokens (class names, IActionResult) so HasCodeSignal
        /// cannot filter them alone.
        /// </summary>
        private static bool LooksLikeSidebarContent(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string lower = text.ToLowerInvariant();

            // Count explicit sidebar marker phrases
            string[] sidebarMarkers =
            {
                "\ncontrollers\n", "\nmodels\n", "\nproperties\n",
                "0 references", "inline bookmarks", "appsettings.",
                "?? inline", "\nviews\n", "\npages\n", "\nwwwroot\n",
                "\nservices\n", "\nrepositories\n", "\ninterfaces\n",
                ".csproj.lscache", "\nmigrations\n", "run and debug",
                "source control"
            };

            int sidebarHits = 0;
            foreach (string marker in sidebarMarkers)
            {
                if (lower.Contains(marker))
                {
                    sidebarHits++;
                }
            }

            // 3+ sidebar markers = almost certainly a file tree dump
            if (sidebarHits >= 3)
            {
                return true;
            }

            // Secondary check: high ratio of short non-code lines (file/folder names)
            string[] allLines = text.Replace("\r\n", "\n").Split('\n');
            if (allLines.Length < 4)
            {
                return false;
            }

            int shortLabelLines = 0;
            int totalNonEmpty = 0;
            foreach (string rawLine in allLines)
            {
                string t = rawLine.Trim();
                if (t.Length == 0)
                {
                    continue;
                }

                totalNonEmpty++;
                // Short line with no code punctuation = likely a label or folder name
                if (t.Length < 35 && !HasCodeSignal(t))
                {
                    shortLabelLines++;
                }
            }

            if (totalNonEmpty == 0)
            {
                return false;
            }

            // >70% of non-empty lines are short non-code labels → sidebar dump
            return (double)shortLabelLines / totalNonEmpty > 0.70;
        }

        private static bool LooksLikeIdeChromeLabelsOnly(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            if (HasCodeSignal(text))
            {
                return false;
            }

            string lower = text.ToLowerInvariant();
            string[] labels =
            {
                "explorer",
                "search",
                "source control",
                "run and debug",
                "extensions",
                "outline",
                "problems",
                "debug console",
                "terminal",
                "ports",
                "output",
                "no tasks in progress",
                "open editors",
                "solution explorer",
                "inline bookmarks"
            };

            int hits = 0;
            foreach (string label in labels)
            {
                if (lower.Contains(label))
                {
                    hits++;
                }
            }

            if (text.Length <= 140 && hits >= 1)
            {
                return true;
            }

            return hits >= 2;
        }

        private static bool HasCodeSignal(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string lower = text.ToLowerInvariant();
            return text.IndexOf('{') >= 0
                || text.IndexOf('}') >= 0
                || text.IndexOf(';') >= 0
                || text.IndexOf("=>", System.StringComparison.Ordinal) >= 0
                || text.IndexOf("::", System.StringComparison.Ordinal) >= 0
                || text.IndexOf("==", System.StringComparison.Ordinal) >= 0
                || text.IndexOf('(') >= 0
                || text.IndexOf(')') >= 0
                || text.IndexOf('_') >= 0
                || lower.Contains("function ")
                || lower.Contains("const ")
                || lower.Contains("let ")
                || lower.Contains("var ")
                || lower.Contains("class ")
                || lower.Contains("public ")
                || lower.Contains("private ")
                || lower.Contains("return ")
                || lower.Contains("if ")
                || lower.Contains("await ")
                || lower.Contains("@param");
        }

        private static bool HasIndentationSignal(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            int indentedLines = 0;

            foreach (string rawLine in lines)
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                int leadingWhitespace = 0;
                while (leadingWhitespace < rawLine.Length
                    && (rawLine[leadingWhitespace] == ' ' || rawLine[leadingWhitespace] == '\t'))
                {
                    leadingWhitespace++;
                }

                if (leadingWhitespace >= 2)
                {
                    indentedLines++;
                    if (indentedLines >= 2)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsVsCodeLike(string processName)
        {
            return processName.Equals("Code", System.StringComparison.OrdinalIgnoreCase)
                || processName.Equals("Cursor", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikePlainVsCodeNotification(string text)
        {
            if (!text.StartsWith("Info:", System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            bool hasCodeSignal =
                text.IndexOf('{') >= 0 ||
                text.IndexOf('}') >= 0 ||
                text.IndexOf(';') >= 0 ||
                text.IndexOf("function", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("const ", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("let ", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("class ", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("=>", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("@param", System.StringComparison.OrdinalIgnoreCase) >= 0;

            return !hasCodeSignal;
        }

        private static string CleanKnownUiNoise(ActiveWindowInfo window, string text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string normalized = text.Replace("\r\n", "\n");
            string[] lines = normalized.Split('\n');
            var cleaned = new StringBuilder();

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.Equals(window.Title, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (ContainsKnownUiNoise(line))
                {
                    continue;
                }

                if (cleaned.Length > 0)
                {
                    cleaned.Append('\n');
                }

                cleaned.Append(line);
                if (cleaned.Length >= maxChars)
                {
                    break;
                }
            }

            string output = cleaned.ToString().Trim();
            if (output.Length > maxChars)
            {
                output = output.Substring(0, maxChars);
            }

            return output;
        }
    }
}


