using System.Windows;
using System.Windows.Controls;
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
                using var editorProcess = System.Diagnostics.Process.GetProcessById(int.Parse(args[2]));
                Win32Native.SetForegroundWindow(editorProcess.MainWindowHandle);
                Thread.Sleep(350);
                var captured = new ContextCaptureEngine().ExecuteCaptureAsync(1).GetAwaiter().GetResult();
                Check(captured.SelectedMethod == CaptureMethod.EditorBridge, "real editor uses bridge as primary selection source");
                Check(captured.SelectedText == args[1], "real editor returns current selection");
                Check(captured.BackgroundContext.Contains("NEIGHBOR_CONTEXT"), "real editor returns unsaved background");
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
            if (args.Contains("--overlay")) RunOverlay();
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
                try
                {
                    await SetTextWithRetryAsync("repeat-copy-test");
                    var copied = await new ClipboardManager().SafeCaptureSelectionAsync(() => SetTextWithRetryAsync("repeat-copy-test"));
                    Check(copied.ClipboardChanged && copied.CapturedText == "repeat-copy-test", "repeated copy of identical text succeeds");
                    var changed = await new ClipboardManager().SafeCaptureSelectionAsync(() => SetTextWithRetryAsync("new-copy-test"));
                    Check(changed.CapturedText == "new-copy-test" && Clipboard.GetText() == "repeat-copy-test", "clipboard contents restored after capture");
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
            catch (Exception ex) { failure = ex; }
            finally { window.Close(); }
        };
        app.Run(window);
        if (failure != null) throw failure;
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

    private static async Task<DataObject> SnapshotClipboardWithRetryAsync()
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < 12; attempt++)
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
            catch (System.Runtime.InteropServices.ExternalException ex) { lastError = ex; await Task.Delay(80); }
        }
        throw new InvalidOperationException("Cannot preserve the clipboard; native test cancelled.", lastError);
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
            CodeExplainer.BackendClient.Configure(new CodeExplainer.ClientConfig { WsBaseUrl = "ws://127.0.0.1:" + port, AuthEnabled = false });
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
        }
        finally
        {
            process.StandardInput.WriteLine("stop");
            if (!process.WaitForExit(5000)) process.Kill();
        }
    }

    private static void RunBridge()
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "simpledocs-pipe-test-" + Guid.NewGuid());
        var info = new System.Diagnostics.ProcessStartInfo("node")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
        };
        info.ArgumentList.Add(System.IO.Path.GetFullPath("editor-extension/test/pipe-fixture.cjs"));
        info.ArgumentList.Add(directory);
        using var process = System.Diagnostics.Process.Start(info)!;
        try
        {
            Check(process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult() == "READY", "Node bridge fixture started");
            var snapshot = EditorBridgeClient.TryCaptureAsync("bridge_selection", directory).GetAwaiter().GetResult();
            Check(snapshot?.BackgroundContext == "before\nbridge_selection\nafter", "C# captures Node named-pipe context with current-user validation");
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
