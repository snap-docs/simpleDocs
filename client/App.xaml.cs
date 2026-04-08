using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Drawing;
using CodeExplainer.Engine;
using CodeExplainer.Engine.Managers;
using CodeExplainer.Engine.Models;

namespace CodeExplainer
{
    public partial class App : System.Windows.Application
    {
        private NotifyIcon? _trayIcon;
        private GlobalHotkeyManager? _hotkeyManager;
        private ContextCaptureEngine? _captureEngine;
        private OverlayWindow? _overlayWindow;
        private MainWindow? _hiddenWindow;
        private int _isExplainInProgress;
        private int _requestSequence;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            RuntimeLog.Info("App", "Startup began.");
            RuntimeLog.Info("App", $"Log file: {RuntimeLog.CurrentLogPath}");

            // Hidden window needed for hotkey message pump
            _hiddenWindow = new MainWindow();
            _hiddenWindow.Show();
            _hiddenWindow.Hide();
            RuntimeLog.Info("App", "Hidden message window created.");

            // Setup system tray icon
            SetupTrayIcon();
            RuntimeLog.Info("App", "Tray icon ready.");

            // Setup new engine
            _captureEngine = new ContextCaptureEngine();

            // Setup hotkey manager (Ctrl+Shift+Space)
            _hotkeyManager = new GlobalHotkeyManager(_hiddenWindow);
            _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
            _hotkeyManager.Register();
            UpdateTrayHotkeyStatus();
            if (_hotkeyManager.IsRegistered)
            {
                RuntimeLog.Info("Hotkey", $"Registered {_hotkeyManager.RegisteredHotkeyLabel}.");
            }
            else
            {
                RuntimeLog.Error("Hotkey", "No global hotkey could be registered.");
            }

            // Create overlay (hidden initially)
            _overlayWindow = new OverlayWindow();
            RuntimeLog.Info("App", "Overlay window created. App is ready.");
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                Visible = true,
                Text = "Code Explainer - starting up"
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Exit", null, (_, _) => ExitApp());
            _trayIcon.ContextMenuStrip = contextMenu;
        }

        private void UpdateTrayHotkeyStatus()
        {
            if (_trayIcon == null || _hotkeyManager == null)
            {
                return;
            }

            if (_hotkeyManager.IsRegistered)
            {
                _trayIcon.Text = $"Code Explainer - {_hotkeyManager.RegisteredHotkeyLabel}";

                if (!string.Equals(_hotkeyManager.RegisteredHotkeyLabel, GlobalHotkeyManager.PreferredHotkeyLabel, StringComparison.Ordinal))
                {
                    _trayIcon.ShowBalloonTip(
                        4000,
                        "Code Explainer",
                        $"Using {_hotkeyManager.RegisteredHotkeyLabel}. {GlobalHotkeyManager.PreferredHotkeyLabel} was already in use.",
                        ToolTipIcon.Info);
                }
            }
            else
            {
                _trayIcon.Text = "Code Explainer - no hotkey";
                _trayIcon.ShowBalloonTip(
                    5000,
                    "Code Explainer",
                    "No global hotkey could be registered. Close conflicting hotkey apps and relaunch.",
                    ToolTipIcon.Warning);
            }
        }

        private async void OnHotkeyPressed(object? sender, EventArgs e)
        {
            if (Interlocked.CompareExchange(ref _isExplainInProgress, 1, 0) != 0)
            {
                RuntimeLog.Warn("Hotkey", "Ignored because a previous explain request is still in progress.");
                _overlayWindow?.ShowMessage(
                    "A previous explain request is still running. Please wait a moment and try again.",
                    "busy");
                return;
            }

            int requestId = Interlocked.Increment(ref _requestSequence);
            string hotkeyLabel = _hotkeyManager?.RegisteredHotkeyLabel ?? "unknown";
            RuntimeLog.Info("Flow", $"req={requestId} stage=hotkey_triggered key=\"{hotkeyLabel}\"");
            var requestTimer = Stopwatch.StartNew();
            try
            {
                await HandleExplainRequest(requestId);
            }
            catch (Exception ex)
            {
                RuntimeLog.Error("App", $"req={requestId} Error handling hotkey: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Error handling hotkey: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _isExplainInProgress, 0);
                requestTimer.Stop();
                RuntimeLog.Info("Flow", $"req={requestId} stage=hotkey_finished duration_ms={requestTimer.ElapsedMilliseconds}");
            }
        }

        private async Task HandleExplainRequest(int requestId)
        {
            if (_captureEngine == null) return;

            await HotkeyReleaseGuard.WaitForTriggerKeysToSettleAsync();

            // Execute the centralized engine capture pipeline
            var captureResult = await _captureEngine.ExecuteCaptureAsync(requestId);
            RuntimeLog.Info(
                "Capture",
                $"req={requestId} process={captureResult.ProcessName} title=\"{RuntimeLog.Preview(captureResult.WindowTitle, 60)}\" env={captureResult.Type.ToApiValue()} " +
                $"selected_method={captureResult.SelectedMethod.ToApiValue()} background_method={captureResult.BackgroundMethod.ToApiValue()} " +
                $"selected_chars={captureResult.SelectedText.Length} background_chars={captureResult.BackgroundContext.Length} " +
                $"is_partial={captureResult.IsPartial} is_unsupported={captureResult.IsUnsupported} status=\"{RuntimeLog.Preview(captureResult.StatusMessage, 120)}\"");

            if (captureResult.HasSelectedText)
            {
                RuntimeLog.Info("Capture", $"req={requestId} Selected preview: {RuntimeLog.Preview(captureResult.SelectedText)}");
            }
            else
            {
                RuntimeLog.Warn("Capture", $"req={requestId} No selected text was captured.");
            }

            if (!string.IsNullOrWhiteSpace(captureResult.BackgroundContext))
            {
                RuntimeLog.Info("Capture", $"req={requestId} Background preview: {RuntimeLog.Preview(captureResult.BackgroundContext)}");
            }

            if (captureResult.IsUnsupported)
            {
                RuntimeLog.Warn("Capture", $"req={requestId} {captureResult.StatusMessage}");
                _overlayWindow?.ShowMessage(
                    captureResult.StatusMessage,
                    $"{captureResult.Type.ToApiValue()} | unsupported");
                return;
            }

            if (captureResult.HasSelectedText)
            {
                RuntimeLog.Info("Backend", $"req={requestId} Sending capture payload to backend.");
                _overlayWindow?.ShowLoading(BuildStatusLabel(captureResult));

                await BackendClient.SendExplainRequest(
                    captureResult.SelectedText,
                    captureResult.BackgroundContext,
                    captureResult.WindowTitle,
                    captureResult.ProcessName,
                    captureResult.Type.ToApiValue(),
                    captureResult.SelectedMethod.ToApiValue(),
                    captureResult.BackgroundMethod.ToApiValue(),
                    captureResult.IsPartial,
                    captureResult.StatusMessage,
                    captureResult.IsUnsupported,
                    token => _overlayWindow?.AppendToken(token),
                    status => _overlayWindow?.SetStatus(status),
                    () => _overlayWindow?.OnStreamComplete(),
                    requestId,
                    ocrUsed:       captureResult.OcrUsed,
                    ocrConfidence: captureResult.OcrConfidence
                );
            }
            else
            {
                RuntimeLog.Warn("Overlay", $"req={requestId} {captureResult.StatusMessage}");
                _overlayWindow?.ShowMessage(
                    captureResult.StatusMessage,
                    BuildStatusLabel(captureResult));
            }
        }

        private static string BuildStatusLabel(CaptureResult captureResult)
        {
            string mode = captureResult.IsPartial ? "partial" : "full";
            return $"{captureResult.Type.ToApiValue()} | {captureResult.SelectedMethod.ToApiValue()} + {captureResult.BackgroundMethod.ToApiValue()} | {mode}";
        }

        private void ExitApp()
        {
            _hotkeyManager?.Unregister();
            _trayIcon?.Dispose();
            _overlayWindow?.Close();
            _hiddenWindow?.Close();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _hotkeyManager?.Unregister();
            _trayIcon?.Dispose();
            base.OnExit(e);
        }
    }
}
