using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using CodeExplainer.Engine.Classifiers;
using CodeExplainer.Engine.Detectors;
using CodeExplainer.Engine.Managers;
using CodeExplainer.Engine.Models;
using CodeExplainer.Engine.Strategies;

namespace CodeExplainer.Engine
{
    /// <summary>
    /// Orchestrates the entire Context Capture Pipeline.
    /// </summary>
    public class ContextCaptureEngine
    {
        private readonly ActiveWindowDetector _detector;
        private readonly EnvironmentClassifier _classifier;
        private readonly Dictionary<EnvironmentType, ICaptureStrategy> _strategies;

        public ContextCaptureEngine()
        {
            _detector = new ActiveWindowDetector();
            _classifier = new EnvironmentClassifier();
            var clipboardManager = new ClipboardManager();
            var compatibilityMode = new ClipboardCompatibilityMode(clipboardManager);

            var browserStrategy = new BrowserStrategy(compatibilityMode);
            var firefoxStrategy = new FirefoxStrategy(compatibilityMode);
            var ideStrategy = new IDEStrategy(compatibilityMode);
            var modernTerminalStrategy = new ModernTerminalStrategy(compatibilityMode);
            var classicTerminalStrategy = new ClassicTerminalStrategy(compatibilityMode);
            var electronStrategy = new ElectronStrategy(compatibilityMode);
            var externalStrategy = new ExternalAppStrategy(compatibilityMode);
            var unknownStrategy = new UnknownAppStrategy(compatibilityMode);

            // Initialize strategies mapped to EnvironmentType
            _strategies = new Dictionary<EnvironmentType, ICaptureStrategy>
            {
                { EnvironmentType.IDE, ideStrategy },
                { EnvironmentType.IDEEmbeddedTerminal, ideStrategy },
                { EnvironmentType.BrowserChromium, browserStrategy },
                { EnvironmentType.BrowserFirefox, firefoxStrategy },
                { EnvironmentType.ModernTerminal, modernTerminalStrategy },
                { EnvironmentType.ClassicTerminal, classicTerminalStrategy },
                { EnvironmentType.Electron, electronStrategy },
                { EnvironmentType.External, externalStrategy },
                { EnvironmentType.Unknown, unknownStrategy }
            };
        }

        /// <summary>
        /// Executes the full capture pipeline based on the active foreground window.
        /// </summary>
        public async Task<CaptureResult> ExecuteCaptureAsync(int requestId)
        {
            try
            {
                // 1. Detect Active Window
                ActiveWindowInfo activeWindow = _detector.GetForegroundEnvironment();
                RuntimeLog.Info(
                    "Window",
                    $"req={requestId} process={activeWindow.ProcessName} title=\"{RuntimeLog.Preview(activeWindow.Title, 60)}\" class={activeWindow.ClassName} hwnd={activeWindow.Hwnd}");
                Debug.WriteLine($"[ContextCaptureEngine] Captured Active Window: {activeWindow}");

                // 2. Classify Environment
                EnvironmentType envType = _classifier.Classify(activeWindow);
                RuntimeLog.Info("Window", $"req={requestId} classified_environment={envType.ToApiValue()}");
                Debug.WriteLine($"[ContextCaptureEngine] Classified Environment as: {envType}");

                // 3. Select Strategy
                if (_strategies.TryGetValue(envType, out ICaptureStrategy? strategy))
                {
                    // 4. Execute Capture
                    RuntimeLog.Info("Capture", $"req={requestId} using_strategy={strategy.GetType().Name}");
                    Debug.WriteLine($"[ContextCaptureEngine] Executing strategy: {strategy.GetType().Name}");
                    var stopwatch = Stopwatch.StartNew();
                    CaptureResult result = await strategy.CaptureAsync(activeWindow);
                    string usageContext = UsageContextBuilder.Build(activeWindow, result.Type);
                    result = new CaptureResult(
                        selectedText: result.SelectedText,
                        backgroundContext: result.BackgroundContext,
                        windowTitle: result.WindowTitle,
                        processName: result.ProcessName,
                        type: result.Type,
                        selectedMethod: result.SelectedMethod,
                        backgroundMethod: result.BackgroundMethod,
                        isPartial: result.IsPartial,
                        isUnsupported: result.IsUnsupported,
                        statusMessage: result.StatusMessage,
                        ocrUsed: result.OcrUsed,
                        ocrConfidence: result.OcrConfidence,
                        usageContext: usageContext);
                    stopwatch.Stop();
                    LogCaptureSummary(requestId, activeWindow, strategy, result, stopwatch.ElapsedMilliseconds);
                    return result;
                }
                
                RuntimeLog.Warn("Capture", "No capture strategy matched. Returning empty result.");
                return CaptureResult.Unsupported(
                    activeWindow,
                    EnvironmentType.Unknown,
                    CaptureMethod.Unsupported,
                    CaptureMethod.WindowMetadata,
                    "No capture strategy matched this window.");
            }
            catch (Exception ex)
            {
                RuntimeLog.Error("Capture", $"Fatal capture exception: {ex.Message}");
                Debug.WriteLine($"[ContextCaptureEngine] Fatal exception in capture pipeline: {ex.Message}");
                var fallbackWindow = new ActiveWindowInfo(IntPtr.Zero, 0, "unknown", string.Empty, string.Empty);
                return CaptureResult.Unsupported(
                    fallbackWindow,
                    EnvironmentType.Unknown,
                    CaptureMethod.Unsupported,
                    CaptureMethod.WindowMetadata,
                    $"Fatal capture exception: {ex.Message}");
            }
        }

        private static void LogCaptureSummary(
            int requestId,
            ActiveWindowInfo activeWindow,
            ICaptureStrategy strategy,
            CaptureResult result,
            long durationMs)
        {
            int selectedChars = result.SelectedText.Length;
            int backgroundChars = result.BackgroundContext.Length;
            string statusPreview = RuntimeLog.Preview(result.StatusMessage, 120);

            RuntimeLog.Info(
                "CaptureSummary",
                $"req={requestId} process={activeWindow.ProcessName} title=\"{RuntimeLog.Preview(activeWindow.Title, 60)}\" env={result.Type.ToApiValue()} " +
                $"strategy={strategy.GetType().Name} selected_method={result.SelectedMethod.ToApiValue()} background_method={result.BackgroundMethod.ToApiValue()} " +
                $"selected_chars={selectedChars} background_chars={backgroundChars} is_partial={result.IsPartial} is_unsupported={result.IsUnsupported} " +
                $"status=\"{statusPreview}\" duration_ms={durationMs}");
        }
    }
}
