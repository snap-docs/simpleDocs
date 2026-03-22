using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Drawing;

namespace CodeExplainer
{
    public partial class App : System.Windows.Application
    {
        private NotifyIcon? _trayIcon;
        private HotkeyManager? _hotkeyManager;
        private OverlayWindow? _overlayWindow;
        private MainWindow? _hiddenWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Hidden window needed for hotkey message pump
            _hiddenWindow = new MainWindow();

            // Setup system tray icon
            SetupTrayIcon();

            // Setup hotkey manager (Ctrl+Space)
            _hotkeyManager = new HotkeyManager(_hiddenWindow);
            _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
            _hotkeyManager.Register();

            // Create overlay (hidden initially)
            _overlayWindow = new OverlayWindow();
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                Visible = true,
                Text = "Code Explainer — Ctrl+Space to explain"
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Exit", null, (_, _) => ExitApp());
            _trayIcon.ContextMenuStrip = contextMenu;
        }

        private async void OnHotkeyPressed(object? sender, EventArgs e)
        {
            try
            {
                await HandleExplainRequest();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling hotkey: {ex.Message}");
            }
        }

        private async Task HandleExplainRequest()
        {
            // Step 1: Detect active application
            var (processName, appType) = AppDetector.GetActiveApp();
            System.Diagnostics.Debug.WriteLine($"Active app: {processName} ({appType})");

            string? selectedText = null;
            string? fullContext = null;
            bool fallbackUsed = false;

            // Step 2: Determine capture strategy based on app type
            bool skipUIA = AppDetector.ShouldSkipUIA(processName);

            if (!skipUIA)
            {
                // Try UIA first for editors, browsers, unknown apps
                try
                {
                    selectedText = UIATextReader.GetSelectedText();
                    fullContext = UIATextReader.GetFullContext();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"UIA failed: {ex.Message}");
                }
            }

            // Step 3: Clipboard fallback if UIA failed or was skipped
            if (string.IsNullOrEmpty(selectedText))
            {
                try
                {
                    bool isTerminal = appType == "terminal";
                    var clipResult = await ClipboardFallback.CaptureViaClipboard(isTerminal);
                    selectedText = clipResult.SelectedText;
                    fullContext = clipResult.FullContext;
                    fallbackUsed = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Clipboard fallback failed: {ex.Message}");
                }
            }

            // Step 4: If we have text, send to backend
            if (!string.IsNullOrWhiteSpace(selectedText))
            {
                // Show overlay with loading state
                _overlayWindow?.ShowLoading();

                // Send to backend and stream response
                await BackendClient.SendExplainRequest(
                    selectedText,
                    fullContext ?? "",
                    appType,
                    token => _overlayWindow?.AppendToken(token),
                    () => _overlayWindow?.OnStreamComplete()
                );
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("No text captured — nothing to explain.");
            }
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
