using System.Threading.Tasks;
using CodeExplainer.Engine.Models;
using System.Text;

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

        public static async Task<SelectedCaptureOutcome> CaptureSelectedTextAsync(
            ActiveWindowInfo window,
            ClipboardCompatibilityMode compatibilityMode,
            bool preferMsaaFirst,
            bool allowMsaaFocusedFallback = true)
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
                    (IsVsCodeLike(window.ProcessName) && UiAutomationCapture.IsEditorContentFocusedElement()) 
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

            // OCR fallback – attempt when UIA/MSAA/clipboard all failed
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

        public static BackgroundCaptureOutcome CaptureEditorBackground(ActiveWindowInfo window, int maxChars = 10000)
        {
            if (UiAutomationCapture.TryGetDocumentRangeText(maxChars, out string documentText))
            {
                if (IsRejectedIdeBackgroundSource(window, documentText))
                {
                    RuntimeLog.Warn("CapturePipeline", $"Rejected raw UIA document-range background for {window.ProcessName}.");
                }
                else
                {
                    string cleaned = CleanKnownUiNoise(window, documentText, maxChars);
                    if (!LooksLikeEditorBackgroundNoise(cleaned) && LooksLikePlausibleEditorBackground(cleaned))
                    {
                        return new BackgroundCaptureOutcome
                        {
                            Text = cleaned,
                            Method = CaptureMethod.UiaTextPatternDocumentRange,
                            Status = "Background context captured via UIA document range."
                        };
                    }
                }

                RuntimeLog.Warn("CapturePipeline", $"Rejected noisy UIA document-range background for {window.ProcessName}.");
            }

            if (UiAutomationCapture.TryGetVisibleRangesText(maxChars, out string visibleText))
            {
                if (IsRejectedIdeBackgroundSource(window, visibleText))
                {
                    RuntimeLog.Warn("CapturePipeline", $"Rejected raw UIA visible-ranges background for {window.ProcessName}.");
                }
                else
                {
                    string cleaned = CleanKnownUiNoise(window, visibleText, maxChars);
                    if (!LooksLikeEditorBackgroundNoise(cleaned) && LooksLikePlausibleEditorBackground(cleaned))
                    {
                        return new BackgroundCaptureOutcome
                        {
                            Text = cleaned,
                            Method = CaptureMethod.UiaTextPatternVisibleRanges,
                            Status = "Background context captured via UIA visible ranges."
                        };
                    }
                }

                RuntimeLog.Warn("CapturePipeline", $"Rejected noisy UIA visible-ranges background for {window.ProcessName}.");
            }

            if (UiAutomationCapture.TryGetNearestContainerText(maxChars, out string containerText))
            {
                if (IsRejectedIdeBackgroundSource(window, containerText))
                {
                    RuntimeLog.Warn("CapturePipeline", $"Rejected raw UIA container background for {window.ProcessName}.");
                }
                else
                {
                    string cleaned = CleanKnownUiNoise(window, containerText, maxChars);
                    if (!LooksLikeEditorBackgroundNoise(cleaned) && LooksLikePlausibleEditorBackground(cleaned))
                    {
                        return new BackgroundCaptureOutcome
                        {
                            Text = cleaned,
                            Method = CaptureMethod.UiaTreeContainer,
                            Status = "Background context captured via UIA editor-local container."
                        };
                    }
                }

                RuntimeLog.Warn("CapturePipeline", $"Rejected noisy UIA container background for {window.ProcessName}.");
            }

            if (MsaaCapture.TryGetContainerText(window.Hwnd, maxChars, out string msaaText))
            {
                if (IsRejectedIdeBackgroundSource(window, msaaText))
                {
                    RuntimeLog.Warn("CapturePipeline", $"Rejected raw MSAA background context for {window.ProcessName}.");
                }
                else
                {
                    string cleaned = CleanKnownUiNoise(window, msaaText, maxChars);
                    if (!LooksLikeEditorBackgroundNoise(cleaned) && LooksLikePlausibleEditorBackground(cleaned))
                    {
                        RuntimeLog.Info("CapturePipeline", $"MSAA background context used for {window.ProcessName}.");
                        return new BackgroundCaptureOutcome
                        {
                            Text = cleaned,
                            Method = CaptureMethod.MsaaContainer,
                            Status = "Background context captured via MSAA container route."
                        };
                    }
                }

                RuntimeLog.Warn("CapturePipeline", $"Rejected noisy MSAA background context for {window.ProcessName}.");
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

            if (HasCodeSignal(normalized))
            {
                return true;
            }

            int lineCount = normalized.Split('\n').Length;
            return lineCount >= 2 && normalized.Length >= 30;
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
                || normalizedText.IndexOf("Braille support is", System.StringComparison.OrdinalIgnoreCase) >= 0;
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
