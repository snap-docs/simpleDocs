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
            if (args.Contains("--bridge")) RunBridge();
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
                var original = new DataObject();
                var sourceClipboard = Clipboard.GetDataObject();
                if (sourceClipboard != null)
                    foreach (var format in sourceClipboard.GetFormats(false))
                    {
                        var data = sourceClipboard.GetData(format, false);
                        if (data != null) original.SetData(format, data);
                    }
                try
                {
                    Clipboard.SetText("repeat-copy-test");
                    var copied = await new ClipboardManager().SafeCaptureSelectionAsync(() => { Clipboard.SetText("repeat-copy-test"); return Task.CompletedTask; });
                    Check(copied.ClipboardChanged && copied.CapturedText == "repeat-copy-test", "repeated copy of identical text succeeds");
                    var changed = await new ClipboardManager().SafeCaptureSelectionAsync(() => { Clipboard.SetText("new-copy-test"); return Task.CompletedTask; });
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
