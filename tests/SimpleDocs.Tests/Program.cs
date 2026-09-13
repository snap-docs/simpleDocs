using System.Windows;
using System.Windows.Controls;
using System.Net.Http;
using CodeExplainer.Engine;
using CodeExplainer.Engine.Managers;
using CodeExplainer.Engine.Models;
using CodeExplainer.Engine.Strategies;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        _checks++;
        Console.WriteLine($"PASS {name}");
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--live-editor"))
            {
                var captured = EditorBridgeClient.TryCaptureAsync(null).GetAwaiter().GetResult();
                Check(captured?.SelectedText == args[1], "real editor bridge returns current selection");
                Check(captured?.BackgroundContext?.Contains("NEIGHBOR_CONTEXT") == true, "real editor bridge returns unsaved background");
                return 0;
            }
            if (args.Contains("--probe-live-editor"))
            {
                var captured = EditorBridgeClient.TryCaptureAsync(null).GetAwaiter().GetResult();
                Check(!string.IsNullOrWhiteSpace(captured?.SelectedText), "live editor bridge returns a non-empty selection");
                Check(captured?.BackgroundContext?.Contains(captured.SelectedText!, StringComparison.Ordinal) == true,
                    "live editor bridge returns selection-anchored context");
                return 0;
            }
            string selected = "const answer = calculateTotal(items);";
            string source = new string('a', 30000) + "\nNEARBY_BEFORE\n" + selected + "\nNEARBY_AFTER\n" + new string('b', 40000);
            string result = ContextTextWindow.AroundSelection(source, selected, 1000);
            Check(result.Length == 1000 && result.Contains(selected) && result.Contains("NEARBY_BEFORE")
                && result.Contains("NEARBY_AFTER"), "large document context stays around the selection");
            Check(ContextTextWindow.AroundSelection(source, selected, 0) == "", "zero context budget");
            Check(ContextTextWindow.AroundSelection("abc", "b", 100) == "abc", "small context preserved");
            Check(ContextTextWindow.AroundSelection("abcde", "abc", 3) == "abc", "selection at document start");
            Check(ContextTextWindow.AroundSelection("abcde", "cde", 3) == "cde", "selection at document end");
            Check(!ContextTextWindow.AddsContext(" a\n b ", "a b"), "whitespace selection echo rejected");
            Check(!ContextTextWindow.AddsContext("abc", "abcdef"), "truncated selection echo rejected");
            Check(ContextTextWindow.AddsContext("before\ncode\nafter", "code"), "surrounding lines accepted");
            Check(EditorBridgeClient.IsValid(new() { SelectedText = selected, BackgroundContext = "before\n" + selected + "\nafter" }, selected), "bridge snapshot matched");
            Check(!EditorBridgeClient.IsValid(new() { SelectedText = "stale", BackgroundContext = source }, selected), "stale bridge snapshot rejected");
            Check(!EditorBridgeClient.IsValid(new() { SelectedText = selected, BackgroundContext = selected }, selected), "bridge echo rejected");
            Check(!EditorBridgeClient.IsValid(new() { SelectedText = selected, BackgroundContext = source }, selected), "oversized bridge snapshot rejected");
            Check(EditorBridgeClient.IsValid(new() { SelectedText = selected, BackgroundContext = selected }, null), "primary bridge accepts selection-only documents");
            Check(!EditorBridgeClient.IsValid(new() { SelectedText = "", BackgroundContext = "" }, null), "primary bridge rejects missing selection");
            Check(EditorBridgeClient.OwnerMatches(null, 100u), "legacy bridge remains eligible during extension upgrade");
            Check(EditorBridgeClient.OwnerMatches(100u, 100u), "bridge owner matches foreground editor");
            Check(!EditorBridgeClient.OwnerMatches(101u, 100u), "background editor bridge is skipped");
            var safeDefaults = new CodeExplainer.ClientConfig();
            Check(safeDefaults.EnvironmentName == "Production" && safeDefaults.ApiBaseUrl.StartsWith("https://")
                && safeDefaults.WsBaseUrl.StartsWith("wss://") && !safeDefaults.AuthEnabled,
                "missing sidecar configuration defaults to hosted no-login production");
            Check(safeDefaults.WebSocketConnectTimeoutSeconds <= 8 && safeDefaults.WebSocketRetryCount == 1,
                "production connection failure is bounded before HTTPS fallback");
            if (args.Contains("--overlay")) RunOverlay();
            if (args.Contains("--desktop-bridge")) RunDesktopBridge().GetAwaiter().GetResult();
            if (args.Contains("--bridge")) RunBridge();
            if (args.Contains("--transport")) RunTransport().GetAwaiter().GetResult();
            if (args.Contains("--native")) RunNative();
            Console.WriteLine($"PASS {_checks} checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"FAIL {ex}"); return 1; }
    }

    private static void RunNative()
    {
        Exception? failure = null;
        var app = new Application();
        var text = new TextBox { AcceptsReturn = true, FontSize = 16, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        string contents = string.Join("\n", Enumerable.Range(0, 1500).Select(i => $"line_{i:D4}: value = {i};"));
        text.Text = contents;
        var window = new Window { Title = "simpleDocs capture test fixture", Width = 800, Height = 600, Content = text };
        window.ContentRendered += async (_, _) =>
        {
            try
            {
                int start = contents.IndexOf("line_1400", StringComparison.Ordinal);
                const int length = 9;
                window.Activate();
                var fixtureHandle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                text.Focus();
                text.Select(start, length);
                text.ScrollToLine(1400);
                await Task.Delay(250);
                var result = await Task.Run(() =>
                {
                    var root = System.Windows.Automation.AutomationElement.FromHandle(fixtureHandle);
                    var editor = root.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                        new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty,
                            System.Windows.Automation.ControlType.Edit));
                    UiAutomationCapture.TryGetSelectedTextFromElement(editor, 5000, out string selected);
                    UiAutomationCapture.TryGetNeighborLinesViaTextRangeFromElement(editor, 6000, 30, 30, out string background);
                    return (SelectedText: selected, BackgroundContext: background);
                });
                Check(result.SelectedText == "line_1400", "native UIA provider captures the actual selection");
                Check(result.BackgroundContext.Contains("line_1399") && result.BackgroundContext.Contains("line_1401"), "native UIA provider captures both neighboring lines late in document");
                Check(text.SelectionStart == start && text.SelectionLength == length, "native capture preserves caret and selection");
                var original = await SnapshotClipboardWithRetryAsync();
                if (original == null)
                {
                    Console.WriteLine("SKIP clipboard mutation checks because another application held the Windows clipboard.");
                }
                else
                {
                    try
                    {
                        await SetTextWithRetryAsync("repeat-copy-test");
                        var copied = await new ClipboardManager().SafeCaptureSelectionAsync(() => SetTextWithRetryAsync("repeat-copy-test"));
                        Check(copied.ClipboardChanged && copied.CapturedText == "repeat-copy-test", "repeated copy of identical text succeeds");
                        var changed = await new ClipboardManager().SafeCaptureSelectionAsync(() => SetTextWithRetryAsync("new-copy-test"));
                        Check(changed.CapturedText == "new-copy-test" && await GetTextWithRetryAsync() == "repeat-copy-test", "clipboard contents restored after capture");
                    }
                    finally
                    {
                        for (int attempt = 0; ; attempt++)
                        {
                            try { Clipboard.SetDataObject(original, true); break; }
                            catch (System.Runtime.InteropServices.ExternalException) when (attempt < 7) { await Task.Delay(80); }
                        }
                    }
                }
            }
            catch (Exception ex) { failure = ex; }
            finally { window.Close(); }
        };
        app.Run(window);
        if (failure != null) throw failure;
    }

    private static async Task RunDesktopBridge()
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "simpledocs-desktop-test-" + Guid.NewGuid());
        var received = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using (var server = new CodeExplainer.EditorRequestServer(request =>
        {
            if (request.SelectedText == "__busy__") return Task.FromResult(false);
            var capture = request.ToCaptureResult();
            if (capture.SelectedMethod != CaptureMethod.EditorBridge || capture.UsageContext != "editor_command")
                throw new Exception("Direct command must bypass native capture.");
            received.Enqueue(capture.SelectedText);
            return Task.FromResult(true);
        }, directory))
        {
            var info = new System.Diagnostics.ProcessStartInfo("node")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add(System.IO.Path.GetFullPath("editor-extension/test/desktop-fixture.cjs"));
            info.ArgumentList.Add(directory);
            using var process = System.Diagnostics.Process.Start(info)!;
            try
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                Check(process.ExitCode == 0, "Node-to-desktop command protocol: " + await output + await error);
                Check(received.SequenceEqual(new[] { "first", "second", "first", "after-busy" }),
                    "desktop receives exact fresh selections; invalid and busy requests are not processed");
            }
            finally { if (!process.HasExited) process.Kill(); }
        }
        Check(!System.IO.Directory.EnumerateFiles(directory).Any(), "desktop removes discovery manifest on shutdown");
        System.IO.Directory.Delete(directory);
    }

    private static void RunOverlay()
    {
        var overlay = new CodeExplainer.OverlayWindow();
        try
        {
            var label = (TextBlock)overlay.FindName("CaseLabel");
            var loading = (FrameworkElement)overlay.FindName("LoadingPanel");
            var feedback = (FrameworkElement)overlay.FindName("FeedbackPanel");
            overlay.OnStreamComplete();
            Check(label.Text == "Error" && loading.Visibility == Visibility.Collapsed,
                "empty completed response ends loading with a retry error");
            overlay.AppendToken("Synthetic explanation");
            overlay.OnStreamComplete();
            Check(label.Text.Contains("Done") && loading.Visibility == Visibility.Collapsed,
                "successful response ends loading");
            overlay.OnStreamError();
            Check(label.Text == "Error" && feedback.Visibility == Visibility.Collapsed,
                "failed response cannot offer success feedback");
        }
        finally { overlay.Close(); }
    }

    private static async Task SetTextWithRetryAsync(string value)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { Clipboard.SetText(value); return; }
            catch (System.Runtime.InteropServices.ExternalException) when (attempt < 12) { await Task.Delay(80); }
        }
    }

    private static async Task<string> GetTextWithRetryAsync()
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
            catch (System.Runtime.InteropServices.ExternalException) when (attempt < 12) { await Task.Delay(80); }
        }
    }

    private static async Task<DataObject?> SnapshotClipboardWithRetryAsync()
    {
        for (int attempt = 0; attempt < 25; attempt++)
        {
            try
            {
                var original = new DataObject();
                var source = Clipboard.GetDataObject();
                if (source != null)
                {
                    foreach (var format in source.GetFormats(false))
                    {
                        var data = source.GetData(format, false);
                        if (data != null) original.SetData(format, data);
                    }
                }
                return original;
            }
            catch (System.Runtime.InteropServices.ExternalException) { await Task.Delay(80); }
        }
        return null;
    }

    private static async Task RunTransport()
    {
        var info = new System.Diagnostics.ProcessStartInfo("node")
        { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(System.IO.Path.GetFullPath("tests/SimpleDocs.Tests/transport-fixture.cjs"));
        using var process = System.Diagnostics.Process.Start(info)!;
        try
        {
            string port = (await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)))!;
            CodeExplainer.BackendClient.Configure(new CodeExplainer.ClientConfig
            {
                ApiBaseUrl = "http://127.0.0.1:" + port,
                WsBaseUrl = "ws://127.0.0.1:" + port,
                AuthEnabled = false,
                WebSocketConnectTimeoutSeconds = 2,
                WebSocketRetryCount = 1
            });
            for (int i = 0; i < 4; i++)
            {
                int completed = 0;
                int errors = 0;
                string status = "";
                var timer = System.Diagnostics.Stopwatch.StartNew();
                await CodeExplainer.BackendClient.SendExplainRequest("synthetic", "", "Test", "test", "unknown", "test", "none",
                    false, "", false, "", "transport-test", "test", onStatus: value => status = value,
                    onComplete: () => completed++, onError: () => errors++);
                Check(timer.Elapsed < TimeSpan.FromSeconds(5), "peer ignoring close cannot block the next request");
                if (i == 0)
                    Check(completed == 0 && errors == 1 && status == "Error", "provider failure ends loading without marking Done");
                else if (i == 2)
                    Check(completed == 0 && errors == 1 && status == "Connection error", "early close ends loading with an error");
                else
                    Check(completed == 1 && errors == 0, "next request completes exactly once after previous failure");
            }

            CodeExplainer.BackendClient.Configure(new CodeExplainer.ClientConfig
            {
                ApiBaseUrl = "http://127.0.0.1:" + port,
                WsBaseUrl = "ws://127.0.0.1:1",
                AuthEnabled = false,
                WebSocketConnectTimeoutSeconds = 2,
                WebSocketRetryCount = 1
            });
            int fallbackCompleted = 0;
            int fallbackErrors = 0;
            string fallbackText = "";
            var fallbackTimer = System.Diagnostics.Stopwatch.StartNew();
            await CodeExplainer.BackendClient.SendExplainRequest("synthetic", "", "Test", "test", "unknown", "test", "none",
                false, "", false, "", "transport-http-fallback", "test", onToken: value => fallbackText += value,
                onComplete: () => fallbackCompleted++, onError: () => fallbackErrors++);
            Check(fallbackTimer.Elapsed < TimeSpan.FromSeconds(5), "blocked WebSocket switches to HTTPS without a long retry delay");
            Check(fallbackCompleted == 1 && fallbackErrors == 0 && fallbackText == "HTTP fallback explanation",
                "HTTPS fallback returns an explanation and completes exactly once");

            CodeExplainer.BackendClient.Configure(new CodeExplainer.ClientConfig
            {
                ApiBaseUrl = "http://127.0.0.1:3000",
                WsBaseUrl = "ws://127.0.0.1:3000",
                AuthEnabled = false
            });
            Check(CodeExplainer.BackendClient.BuildConnectionErrorMessage(new HttpRequestException()).Contains("local development server"),
                "local configuration failure identifies the missing local server");
        }
        finally
        {
            process.StandardInput.WriteLine("stop");
            if (!process.WaitForExit(5000)) process.Kill();
        }
    }

    private static void RunBridge()
    {
        RunBridgeProtocol(1, "legacy bridge serves primary capture during extension upgrade");
        RunBridgeProtocol(3, "current bridge serves primary capture and prunes stale endpoints");
    }

    private static void RunBridgeProtocol(int protocol, string checkName)
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "simpledocs-pipe-test-" + Guid.NewGuid());
        var info = new System.Diagnostics.ProcessStartInfo("node")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
        };
        info.ArgumentList.Add(System.IO.Path.GetFullPath("editor-extension/test/pipe-fixture.cjs"));
        info.ArgumentList.Add(directory);
        info.ArgumentList.Add(protocol.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var process = System.Diagnostics.Process.Start(info)!;
        try
        {
            Check(process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult() == "READY", "Node bridge fixture started");
            string staleManifest = System.IO.Path.Combine(directory, "simpleDocs-editor-2147483646-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.json");
            System.IO.File.WriteAllText(staleManifest,
                "{\"pipe\":\"simpleDocs-editor-2147483646-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"token\":\"" + new string('b', 64) + "\"}");
            System.IO.File.SetLastWriteTimeUtc(staleManifest, DateTime.UtcNow.AddMinutes(-5));
            var snapshot = EditorBridgeClient.TryCaptureAsync(null, directory).GetAwaiter().GetResult();
            Check(snapshot?.SelectedText == "bridge_selection"
                && snapshot.BackgroundContext == "before\nbridge_selection\nafter", checkName);
            Check(!System.IO.File.Exists(staleManifest), "dead bridge manifest is removed without touching live endpoints");
        }
        finally
        {
            process.StandardInput.WriteLine("stop");
            if (!process.WaitForExit(5000)) process.Kill();
            if (System.IO.Directory.Exists(directory) && !System.IO.Directory.EnumerateFileSystemEntries(directory).Any())
                System.IO.Directory.Delete(directory);
        }
    }
}
