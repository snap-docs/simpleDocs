using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Drawing;
using CodeExplainer.ContextCapture.Vision;
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
        private IVisionCaptureService? _visionCaptureService;
        private WindowsStartupManager? _startupManager;
        private ToolStripMenuItem? _signInMenuItem;
        private ToolStripMenuItem? _manageMethodsMenuItem;
        private ToolStripMenuItem? _logoutMenuItem;
        private ToolStripMenuItem? _startupMenuItem;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            RuntimeLog.Info("App", "Startup began.");
            RuntimeLog.Info("App", $"Log file: {RuntimeLog.CurrentLogPath}");
            RuntimeLog.Info("App", $"Version: {RuntimeLog.AppVersion}");
            _config = ClientConfig.Load();
            BackendClient.Configure(_config);
            _authSessionManager = new AuthSessionManager(_config);
            _startupManager = new WindowsStartupManager();
            RuntimeLog.Info("App", $"Environment={_config.EnvironmentName} api={_config.ApiBaseUrl} ws={_config.WsBaseUrl} auth_enabled={_config.AuthEnabled}");

            try
            {
                _startupManager.Initialize();
                RuntimeLog.Info("Startup", $"Windows auto-start enabled={_startupManager.IsStartupEnabled}");
            }
            catch (Exception ex)
            {
                RuntimeLog.Error("Startup", $"Unable to configure Windows auto-start: {ex.Message}");
            }

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
            _visionCaptureService = new VisionCaptureService(_config);

            // Create overlay (hidden initially)
            _overlayWindow = new OverlayWindow();
            _overlayWindow.FeedbackHandler = SubmitFeedbackAsync;

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
            _startupMenuItem = new ToolStripMenuItem("Start On Windows Login", null, (_, _) => ToggleStartupFromTray());
            _signInMenuItem = new ToolStripMenuItem("Sign In", null, async (_, _) => await SignInFromTrayAsync());
            _manageMethodsMenuItem = new ToolStripMenuItem("Manage Sign-In Methods", null, async (_, _) => await ManageAuthMethodsFromTrayAsync());
            _logoutMenuItem = new ToolStripMenuItem("Logout", null, async (_, _) => await LogoutFromTrayAsync());
            contextMenu.Items.Add(_startupMenuItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(_signInMenuItem);
            contextMenu.Items.Add(_manageMethodsMenuItem);
            contextMenu.Items.Add(_logoutMenuItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Exit", null, (_, _) => ExitApp());
            _trayIcon.ContextMenuStrip = contextMenu;
            UpdateTrayAuthStatus();
            UpdateTrayStartupStatus();
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

            if (_manageMethodsMenuItem != null)
            {
                _manageMethodsMenuItem.Enabled = authEnabled && hasSession;
            }
        }

        private void UpdateTrayStartupStatus()
        {
            if (_startupMenuItem == null || _startupManager == null)
            {
                return;
            }

            _startupMenuItem.Checked = _startupManager.IsStartupEnabled;
        }

        private void ToggleStartupFromTray()
        {
            if (_startupManager == null)
            {
                return;
            }

            try
            {
                bool nextState = !_startupManager.IsStartupEnabled;
                bool enabled = _startupManager.SetStartupEnabled(nextState);
                UpdateTrayStartupStatus();
                RuntimeLog.Info("Startup", $"Windows auto-start updated enabled={enabled}");
                _trayIcon?.ShowBalloonTip(
                    2500,
                    "simpleDocs",
                    enabled ? "simpleDocs will now start when you sign in to Windows." : "simpleDocs will no longer start automatically with Windows.",
                    ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                RuntimeLog.Error("Startup", $"Failed to update Windows auto-start: {ex.Message}");
                _trayIcon?.ShowBalloonTip(
                    3000,
                    "simpleDocs",
                    "Unable to update the Windows startup setting on this machine.",
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
            System.Drawing.Point cursorPosition = Control.MousePosition;
            Task<CaptureResult> textCaptureTask = _captureEngine.ExecuteCaptureAsync(requestId);
            Task<VisionCaptureResult?> visionCaptureTask = TryCaptureVisionAsync(requestId, cursorPosition);

            CaptureResult captureResult = await textCaptureTask;
            VisionCaptureResult? visionResult = await visionCaptureTask;

            RuntimeLog.Info(
                "Capture",
                $"req={requestId} process={captureResult.ProcessName} title=\"{RuntimeLog.Preview(captureResult.WindowTitle, 60)}\" env={captureResult.Type.ToApiValue()} " +
                $"selected_method={captureResult.SelectedMethod.ToApiValue()} background_method={captureResult.BackgroundMethod.ToApiValue()} " +
                $"selected_chars={captureResult.SelectedText.Length} background_chars={captureResult.BackgroundContext.Length} " +
                $"is_partial={captureResult.IsPartial} is_unsupported={captureResult.IsUnsupported} status=\"{RuntimeLog.Preview(captureResult.StatusMessage, 120)}\"");

            if (captureResult.HasSelectedText)
            {
                RuntimeLog.Info("Capture", $"req={requestId} Selected preview: {RuntimeLog.Preview(captureResult.SelectedText)}");
                RuntimeLog.Info("Capture", $"req={requestId} Selected full: {EscapeForSingleLineLog(captureResult.SelectedText)}");
            }
            else
            {
                RuntimeLog.Warn("Capture", $"req={requestId} No selected text was captured.");
            }

            if (!string.IsNullOrWhiteSpace(captureResult.BackgroundContext))
            {
                RuntimeLog.Info("Capture", $"req={requestId} Background preview: {RuntimeLog.Preview(captureResult.BackgroundContext)}");
                RuntimeLog.Info("Capture", $"req={requestId} Background full: {EscapeForSingleLineLog(captureResult.BackgroundContext)}");
            }

            if (visionResult?.HasAnyImage == true)
            {
                RuntimeLog.Info(
                    "Vision",
                    $"req={requestId} cursor_bytes={visionResult.CursorRegionPng.Length} panel_bytes={visionResult.ActivePanelPng.Length} window_bytes={visionResult.FullWindowPng.Length} ocr_chars={visionResult.ExtractedTextOcr.Length}");
                if (!string.IsNullOrWhiteSpace(_config?.VisionDebugDirectory))
                {
                    VisionDebugExporter.Export(_config!.VisionDebugDirectory!, requestId.ToString(), visionResult);
                }
            }

            bool hasVisionPayload = visionResult?.HasAnyImage == true;
            if (captureResult.IsUnsupported && !hasVisionPayload)
            {
                RuntimeLog.Warn("Capture", $"req={requestId} {captureResult.StatusMessage}");
                _overlayWindow?.ShowMessage(
                    captureResult.StatusMessage,
                    $"{captureResult.Type.ToApiValue()} | unsupported");
                return;
            }

            if (!captureResult.HasSelectedText && !hasVisionPayload)
            {
                RuntimeLog.Warn("Overlay", $"req={requestId} {captureResult.StatusMessage}");
                _overlayWindow?.ShowMessage(
                    captureResult.StatusMessage,
                    BuildStatusLabel(captureResult));
                return;
            }

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
            ExplainStreamRequest request = BuildExplainStreamRequest(
                streamRequestId,
                captureResult,
                visionResult,
                cursorPosition);

            RuntimeLog.Info("Backend", $"req={requestId} Sending capture payload to backend.");
            _overlayWindow?.ShowLoading(BuildStatusLabel(captureResult, request.CaptureMethodExtended), streamRequestId);

            await BackendClient.SendExplainRequest(
                request,
                accessToken,
                token => _overlayWindow?.AppendToken(token),
                status => _overlayWindow?.SetStatus(status),
                () => _overlayWindow?.OnStreamComplete());
        }

        private async Task<VisionCaptureResult?> TryCaptureVisionAsync(int requestId, System.Drawing.Point cursorPosition)
        {
            if (_config?.EnableVisionPipeline != true || _visionCaptureService == null)
            {
                return null;
            }

            try
            {
                return await _visionCaptureService.CaptureAsync(
                    new System.Windows.Point(cursorPosition.X, cursorPosition.Y),
                    CaptureMode.AllLayers);
            }
            catch (Exception ex)
            {
                RuntimeLog.Warn("Vision", $"req={requestId} capture failed: {ex.Message}");
                return null;
            }
        }

        private static ExplainStreamRequest BuildExplainStreamRequest(
            string requestId,
            CaptureResult captureResult,
            VisionCaptureResult? visionResult,
            System.Drawing.Point cursorPosition)
        {
            bool hasVisionPayload = visionResult?.HasAnyImage == true;
            string? selectedText = captureResult.HasSelectedText ? captureResult.SelectedText : null;
            string backgroundContext = !string.IsNullOrWhiteSpace(captureResult.BackgroundContext)
                ? captureResult.BackgroundContext
                : visionResult?.BackgroundContextUia ?? string.Empty;

            return new ExplainStreamRequest
            {
                RequestId = requestId,
                UsageContext = captureResult.UsageContext,
                SelectedText = selectedText,
                BackgroundContext = backgroundContext,
                OcrText = visionResult?.ExtractedTextOcr,
                WindowTitle = captureResult.WindowTitle,
                ProcessName = captureResult.ProcessName,
                EnvironmentType = captureResult.Type.ToApiValue(),
                SelectedMethod = captureResult.SelectedMethod.ToApiValue(),
                BackgroundMethod = captureResult.BackgroundMethod.ToApiValue(),
                CaptureMethodExtended = DetermineCaptureMethodExtended(captureResult, hasVisionPayload),
                IsPartial = captureResult.IsPartial,
                IsUnsupported = captureResult.IsUnsupported && !hasVisionPayload,
                StatusMessage = captureResult.StatusMessage,
                OcrUsed = captureResult.OcrUsed || !string.IsNullOrWhiteSpace(visionResult?.ExtractedTextOcr),
                OcrConfidence = captureResult.OcrConfidence,
                Vision = visionResult,
                CursorPosition = new CursorPositionPayload
                {
                    X = cursorPosition.X,
                    Y = cursorPosition.Y
                }
            };
        }

        private static string DetermineCaptureMethodExtended(CaptureResult captureResult, bool hasVisionPayload)
        {
            if (captureResult.HasSelectedText)
            {
                return hasVisionPayload ? "vision_augmented" : "text_only";
            }

            return hasVisionPayload ? "vision_only" : "text_only";
        }

        private async Task<bool> SubmitFeedbackAsync(string requestId, string reaction)
        {
            if (_authSessionManager == null || _config?.AuthEnabled == false)
            {
                RuntimeLog.Warn("Feedback", $"request_id={requestId} reaction={reaction} auth_not_available");
                return false;
            }

            try
            {
                string accessToken = await _authSessionManager.EnsureValidAccessTokenAsync();
                await BackendClient.SendFeedbackAsync(requestId, reaction, accessToken);
                return true;
            }
            catch (SessionExpiredException ex)
            {
                RuntimeLog.Warn("Feedback", $"request_id={requestId} reaction={reaction} session_expired=\"{ex.Message}\"");
                return false;
            }
            catch (Exception ex)
            {
                RuntimeLog.Error("Feedback", $"request_id={requestId} reaction={reaction} failed=\"{ex.Message}\"");
                return false;
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
            bool isFirstPrompt = true;
            while (true)
            {
                var loginWindow = new LoginWindow();
                if (isFirstPrompt)
                {
                    // The opening message is guidance ("sign in to continue"),
                    // not a failure, so present it in a neutral style.
                    loginWindow.SetInfo(reason);
                    isFirstPrompt = false;
                }
                else
                {
                    loginWindow.SetError(reason);
                }

                bool? result = loginWindow.ShowDialog();
                if (result != true || loginWindow.Submission == null)
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

                    await ExecuteLoginSubmissionAsync(loginWindow.Submission);
                    UpdateTrayAuthStatus();
                    return true;
                }
                catch (AuthApiException ex)
                {
                    RuntimeLog.Warn("Auth", $"Interactive sign-in failed: {ex.Message}");
                    reason = ex.Message;
                }
                catch (BrowserAuthException ex)
                {
                    RuntimeLog.Warn("Auth", $"Browser sign-in failed locally: {ex.Message}");
                    reason = ex.Message;
                }
                catch (Exception ex)
                {
                    RuntimeLog.Error("Auth", $"Interactive sign-in failed: {ex.Message}");
                    reason = "Unable to complete sign-in. Check your connection and try again.";
                }
            }
        }

        private async Task ExecuteLoginSubmissionAsync(AuthWindowSubmission submission)
        {
            if (_authSessionManager == null)
            {
                throw new InvalidOperationException("The auth session manager is unavailable.");
            }

            switch (submission.Kind)
            {
                case AuthWindowSubmissionKind.RedeemCodeLogin:
                    await _authSessionManager.RedeemCodeAsync(submission.Code);
                    RuntimeLog.Info("Auth", "Redeem-code sign-in completed.");
                    break;
                case AuthWindowSubmissionKind.GoogleLogin:
                    await _authSessionManager.SignInWithGoogleAsync();
                    RuntimeLog.Info("Auth", "Google sign-in completed.");
                    break;
                case AuthWindowSubmissionKind.EmailPasswordLogin:
                    await _authSessionManager.SignInWithEmailPasswordAsync(submission.Email, submission.Password);
                    RuntimeLog.Info("Auth", "Email/password sign-in completed.");
                    break;
                case AuthWindowSubmissionKind.EmailPasswordRegister:
                    await _authSessionManager.RegisterWithEmailPasswordAsync(submission.Email, submission.Password, submission.DisplayName);
                    RuntimeLog.Info("Auth", "Email/password registration completed.");
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported login submission kind: {submission.Kind}");
            }
        }

        private async Task ManageAuthMethodsFromTrayAsync()
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

            bool authenticated = await EnsureAuthenticatedAsync(interactive: true, "Sign in to manage your account methods.");
            if (!authenticated)
            {
                return;
            }

            string reason = string.Empty;
            while (true)
            {
                AuthStateResponse authState;
                try
                {
                    authState = await _authSessionManager.GetCurrentAuthStateAsync();
                }
                catch (SessionExpiredException ex)
                {
                    RuntimeLog.Warn("Auth", $"Account-method load expired the session: {ex.Message}");
                    bool reauthenticated = await EnsureAuthenticatedAsync(interactive: true, "Sign in again to manage your account methods.");
                    if (!reauthenticated)
                    {
                        return;
                    }

                    reason = ex.Message;
                    continue;
                }

                var accountWindow = new AccountMethodsWindow(authState);
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    accountWindow.SetError(reason);
                }

                bool? result = accountWindow.ShowDialog();
                if (result != true || accountWindow.Submission == null)
                {
                    return;
                }

                try
                {
                    await ExecuteAccountMethodSubmissionAsync(accountWindow.Submission);
                    UpdateTrayAuthStatus();
                    _trayIcon?.ShowBalloonTip(2500, "simpleDocs", $"{DescribeSubmission(accountWindow.Submission)} was added to this account.", ToolTipIcon.Info);
                    return;
                }
                catch (AuthApiException ex)
                {
                    RuntimeLog.Warn("Auth", $"Account linking failed: {ex.Message}");
                    reason = ex.Message;
                }
                catch (BrowserAuthException ex)
                {
                    RuntimeLog.Warn("Auth", $"Browser account linking failed locally: {ex.Message}");
                    reason = ex.Message;
                }
                catch (Exception ex)
                {
                    RuntimeLog.Error("Auth", $"Account linking failed: {ex.Message}");
                    reason = "Unable to update account methods right now. Try again in a moment.";
                }
            }
        }

        private async Task ExecuteAccountMethodSubmissionAsync(AuthWindowSubmission submission)
        {
            if (_authSessionManager == null)
            {
                throw new InvalidOperationException("The auth session manager is unavailable.");
            }

            switch (submission.Kind)
            {
                case AuthWindowSubmissionKind.GoogleLink:
                    await _authSessionManager.LinkGoogleAsync();
                    RuntimeLog.Info("Auth", "Google method linked to the current account.");
                    break;
                case AuthWindowSubmissionKind.RedeemCodeLink:
                    await _authSessionManager.LinkRedeemCodeAsync(submission.Code);
                    RuntimeLog.Info("Auth", "Redeem-code method linked to the current account.");
                    break;
                case AuthWindowSubmissionKind.EmailPasswordLink:
                    await _authSessionManager.LinkEmailPasswordAsync(submission.Email, submission.Password, submission.DisplayName);
                    RuntimeLog.Info("Auth", "Email/password method linked to the current account.");
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported account submission kind: {submission.Kind}");
            }
        }

        private static string DescribeSubmission(AuthWindowSubmission submission)
        {
            return submission.Kind switch
            {
                AuthWindowSubmissionKind.GoogleLink => "Google sign-in",
                AuthWindowSubmissionKind.RedeemCodeLink => "Redeem code",
                AuthWindowSubmissionKind.EmailPasswordLink => "Email/password sign-in",
                _ => "The account method"
            };
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
            _trayIcon?.ShowBalloonTip(2500, "simpleDocs", "You are signed out.", ToolTipIcon.Info);
        }

        private static string BuildStatusLabel(CaptureResult captureResult, string? captureMethodExtended = null)
        {
            string mode = captureResult.IsPartial ? "partial" : "full";
            string extended = string.IsNullOrWhiteSpace(captureMethodExtended) ? string.Empty : $" | {captureMethodExtended}";
            return $"{captureResult.Type.ToApiValue()} | {captureResult.SelectedMethod.ToApiValue()} + {captureResult.BackgroundMethod.ToApiValue()} | {mode}{extended}";
        }

        private static string EscapeForSingleLineLog(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "<empty>";
            }

            return text
                .Replace("\\", "\\\\")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Trim();
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
