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
        private ClientConfig? _config;
        private AuthSessionManager? _authSessionManager;
        private ToolStripMenuItem? _signInMenuItem;
        private ToolStripMenuItem? _logoutMenuItem;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            RuntimeLog.Info("App", "Startup began.");
            RuntimeLog.Info("App", $"Log file: {RuntimeLog.CurrentLogPath}");
            RuntimeLog.Info("App", $"Version: {RuntimeLog.AppVersion}");
            _config = ClientConfig.Load();
            BackendClient.Configure(_config);
            _authSessionManager = new AuthSessionManager(_config);
            RuntimeLog.Info("App", $"Environment={_config.EnvironmentName} api={_config.ApiBaseUrl} ws={_config.WsBaseUrl} auth_enabled={_config.AuthEnabled}");

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

            // Create overlay (hidden initially)
            _overlayWindow = new OverlayWindow();

            bool authenticated = await EnsureAuthenticatedAsync(interactive: true, "Sign in is required to start simpleDocs.");
            if (!authenticated)
            {
                RuntimeLog.Warn("Auth", "Startup aborted because sign-in was not completed.");
                ExitApp();
                return;
            }

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
            RuntimeLog.Info("App", "Overlay window created. App is ready.");
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                Visible = true,
                Text = "simpleDocs - starting up"
            };

            var contextMenu = new ContextMenuStrip();
            _signInMenuItem = new ToolStripMenuItem("Sign In", null, async (_, _) => await SignInFromTrayAsync());
            _logoutMenuItem = new ToolStripMenuItem("Logout", null, async (_, _) => await LogoutFromTrayAsync());
            contextMenu.Items.Add(_signInMenuItem);
            contextMenu.Items.Add(_logoutMenuItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Exit", null, (_, _) => ExitApp());
            _trayIcon.ContextMenuStrip = contextMenu;
            UpdateTrayAuthStatus();
        }

        private void UpdateTrayHotkeyStatus()
        {
            if (_trayIcon == null || _hotkeyManager == null)
            {
                return;
            }

            if (_hotkeyManager.IsRegistered)
            {
                _trayIcon.Text = $"simpleDocs - {_hotkeyManager.RegisteredHotkeyLabel}";

                if (!string.Equals(_hotkeyManager.RegisteredHotkeyLabel, GlobalHotkeyManager.PreferredHotkeyLabel, StringComparison.Ordinal))
                {
                    _trayIcon.ShowBalloonTip(
                        4000,
                        "simpleDocs",
                        $"Using {_hotkeyManager.RegisteredHotkeyLabel}. {GlobalHotkeyManager.PreferredHotkeyLabel} was already in use.",
                        ToolTipIcon.Info);
                }
            }
            else
            {
                _trayIcon.Text = "simpleDocs - no hotkey";
                _trayIcon.ShowBalloonTip(
                    5000,
                    "simpleDocs",
                    "No global hotkey could be registered. Close conflicting hotkey apps and relaunch.",
                    ToolTipIcon.Warning);
            }
        }

        private void UpdateTrayAuthStatus()
        {
            if (_trayIcon == null)
            {
                return;
            }

            bool hasSession = _authSessionManager?.HasStoredSession == true;
            bool authEnabled = _config?.AuthEnabled != false;
            if (_signInMenuItem != null)
            {
                _signInMenuItem.Enabled = authEnabled && !hasSession;
            }

            if (_logoutMenuItem != null)
            {
                _logoutMenuItem.Enabled = authEnabled && hasSession;
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
                bool authenticated = await EnsureAuthenticatedAsync(interactive: true, "Sign in is required before sending a request.");
                if (!authenticated)
                {
                    _overlayWindow?.ShowMessage("Sign-in is required before you can request an explanation.", "auth required");
                    return;
                }

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
            if (_authSessionManager == null) return;

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
                string accessToken = string.Empty;
                if (_config?.AuthEnabled != false)
                {
                    try
                    {
                        accessToken = await _authSessionManager.EnsureValidAccessTokenAsync();
                    }
                    catch (SessionExpiredException ex)
                    {
                        RuntimeLog.Warn("Auth", $"req={requestId} session expired while preparing backend request: {ex.Message}");
                        bool reauthenticated = await EnsureAuthenticatedAsync(interactive: true, "Your session expired. Sign in again to continue.");
                        if (!reauthenticated)
                        {
                            _overlayWindow?.ShowMessage("Your session expired. Sign in again to continue.", "auth required");
                            return;
                        }

                        accessToken = await _authSessionManager.EnsureValidAccessTokenAsync();
                    }
                }

                string streamRequestId = _authSessionManager.BuildRequestId(requestId);
                RuntimeLog.Info("Backend", $"req={requestId} Sending capture payload to backend.");
                _overlayWindow?.ShowLoading(BuildStatusLabel(captureResult), requestId);

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
                    accessToken,
                    _authSessionManager.SessionId,
                    streamRequestId,
                    captureResult.UsageContext,
                    token => _overlayWindow?.AppendToken(token),
                    status => _overlayWindow?.SetStatus(status),
                    () => _overlayWindow?.OnStreamComplete(),
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

        private async Task<bool> EnsureAuthenticatedAsync(bool interactive, string reason)
        {
            if (_authSessionManager == null)
            {
                return false;
            }

            if (_config?.AuthEnabled == false)
            {
                UpdateTrayAuthStatus();
                return true;
            }

            try
            {
                await _authSessionManager.EnsureValidAccessTokenAsync();
                UpdateTrayAuthStatus();
                return true;
            }
            catch (SessionExpiredException)
            {
                // Fall through to restore/login logic below.
            }

            bool restored = await _authSessionManager.TryRestoreSessionAsync();
            if (restored)
            {
                UpdateTrayAuthStatus();
                return true;
            }

            if (!interactive)
            {
                UpdateTrayAuthStatus();
                return false;
            }

            return await PromptForLoginAsync(reason);
        }

        private async Task<bool> PromptForLoginAsync(string reason)
        {
            RuntimeLog.Info("Auth", reason);
            while (true)
            {
                var loginWindow = new LoginWindow();
                loginWindow.SetError(reason);
                bool? result = loginWindow.ShowDialog();
                if (result != true)
                {
                    UpdateTrayAuthStatus();
                    return false;
                }

                try
                {
                    if (_authSessionManager == null)
                    {
                        return false;
                    }

                    await _authSessionManager.RedeemCodeAsync(loginWindow.RedeemCode);
                    RuntimeLog.Info("Auth", "Redeem code accepted.");
                    UpdateTrayAuthStatus();
                    return true;
                }
                catch (AuthApiException ex)
                {
                    RuntimeLog.Warn("Auth", $"Redeem code failed: {ex.Message}");
                    reason = ex.Message;
                }
                catch (Exception ex)
                {
                    RuntimeLog.Error("Auth", $"Redeem code request failed: {ex.Message}");
                    reason = "Unable to reach the backend. Check your connection and try again.";
                }
            }
        }

        private async Task SignInFromTrayAsync()
        {
            if (_config?.AuthEnabled == false)
            {
                _trayIcon?.ShowBalloonTip(2500, "simpleDocs", "Sign-in is disabled in the current environment.", ToolTipIcon.Info);
                return;
            }

            bool authenticated = await EnsureAuthenticatedAsync(interactive: true, "Sign in to continue.");
            if (authenticated && _trayIcon != null)
            {
                _trayIcon.ShowBalloonTip(2500, "simpleDocs", "You are signed in.", ToolTipIcon.Info);
            }
        }

        private async Task LogoutFromTrayAsync()
        {
            if (_config?.AuthEnabled == false)
            {
                _trayIcon?.ShowBalloonTip(2500, "simpleDocs", "Sign-in is disabled in the current environment.", ToolTipIcon.Info);
                return;
            }

            if (_authSessionManager == null)
            {
                return;
            }

            await _authSessionManager.LogoutAsync();
            UpdateTrayAuthStatus();
            _overlayWindow?.ShowMessage("You have been signed out.", "signed out");
            bool authenticated = await EnsureAuthenticatedAsync(interactive: true, "Enter a redeem code to sign in again.");
            if (!authenticated)
            {
                _trayIcon?.ShowBalloonTip(3000, "simpleDocs", "You are currently signed out.", ToolTipIcon.Warning);
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
