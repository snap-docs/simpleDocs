using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace CodeExplainer.Engine.Managers
{
    public class ClipboardManager
    {
        internal const int SelectedTextLimit = 12000;
        private static readonly SemaphoreSlim CaptureLock = new(1, 1);

        public class ClipboardCaptureOutcome
        {
            public string? CapturedText { get; init; }
            public bool ClipboardChanged { get; init; }
            public bool ClipboardMutatedDuringRequest { get; init; }
            public bool DiffersFromPreviousText { get; init; }
        }

        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();

        public async Task<ClipboardCaptureOutcome> SafeCaptureSelectionAsync(Func<Task> captureAction)
        {
            await CaptureLock.WaitAsync();
            try
            {
                return await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    DataObject? snapshot = null;
                    uint before = GetClipboardSequenceNumber();
                    uint capturedSequence = before;
                    bool restore = false;
                    string? previousText = null;
                    try
                    {
                        // Materialize delayed clipboard data before the source application replaces it.
                        (snapshot, previousText) = await MaterializeClipboardWithRetryAsync();
                        if (snapshot == null && GetClipboardSequenceNumber() != 0)
                            return new ClipboardCaptureOutcome();
                        if (GetClipboardSequenceNumber() != before) return new ClipboardCaptureOutcome();
                        await captureAction();

                        var timer = Stopwatch.StartNew();
                        string? captured = null;
                        while (timer.ElapsedMilliseconds < 1200)
                        {
                            capturedSequence = GetClipboardSequenceNumber();
                            if (capturedSequence != before)
                            {
                                restore = true;
                                try
                                {
                                    captured = ExtractClipboardText(Clipboard.GetDataObject(), SelectedTextLimit);
                                    if (!string.IsNullOrWhiteSpace(captured)) break;
                                }
                                catch (ExternalException) { }
                            }
                            await Task.Delay(60);
                        }

                        return new ClipboardCaptureOutcome
                        {
                            CapturedText = captured,
                            ClipboardChanged = capturedSequence != before && !string.IsNullOrWhiteSpace(captured),
                            ClipboardMutatedDuringRequest = capturedSequence != before,
                            DiffersFromPreviousText = !string.Equals(captured, previousText, StringComparison.Ordinal)
                        };
                    }
                    catch (Exception ex)
                    {
                        RuntimeLog.Warn("Clipboard", $"Copy capture unavailable: {ex.GetType().Name}");
                        return new ClipboardCaptureOutcome();
                    }
                    finally
                    {
                        // Do not overwrite a newer clipboard update that happened after our read.
                        if (restore)
                        {
                            for (int attempt = 0; attempt < 8; attempt++)
                            {
                                if (GetClipboardSequenceNumber() != capturedSequence) break;
                                try
                                {
                                    if (snapshot != null) Clipboard.SetDataObject(snapshot, true);
                                    else Clipboard.Clear();
                                    break;
                                }
                                catch (ExternalException)
                                {
                                    if (attempt == 7)
                                        RuntimeLog.Warn("Clipboard", "Original clipboard could not be restored after bounded retries.");
                                    else
                                        await Task.Delay(50);
                                }
                            }
                        }
                    }
                }).Task.Unwrap();
            }
            finally { CaptureLock.Release(); }
        }

        internal static string? ExtractClipboardText(IDataObject? data, int maxChars = SelectedTextLimit)
        {
            if (data == null || maxChars <= 0) return null;

            foreach (string format in new[] { DataFormats.UnicodeText, DataFormats.Text })
            {
                if (data.GetDataPresent(format) && data.GetData(format) is string text && !string.IsNullOrWhiteSpace(text))
                    return Bound(text, maxChars);
            }

            return data.GetDataPresent(DataFormats.Html) && data.GetData(DataFormats.Html) is string html
                ? ExtractEquationFromHtml(html, maxChars)
                : null;
        }

        internal static string? ExtractEquationFromHtml(string? html, int maxChars = SelectedTextLimit)
        {
            if (string.IsNullOrWhiteSpace(html) || maxChars <= 0) return null;

            Match annotation = Regex.Match(html,
                "<annotation\\b[^>]*encoding\\s*=\\s*['\"]application/(?:x-)?tex['\"][^>]*>(.*?)</annotation>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (annotation.Success)
                return Bound(WebUtility.HtmlDecode(StripTags(annotation.Groups[1].Value)), maxChars);

            Match altText = Regex.Match(html, "<math\\b[^>]*\\balttext\\s*=\\s*(['\"])(.*?)\\1",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (altText.Success)
                return Bound(WebUtility.HtmlDecode(altText.Groups[2].Value), maxChars);

            return null;
        }

        private static string StripTags(string value) => Regex.Replace(value, "<[^>]+>", string.Empty);

        private static string? Bound(string value, int maxChars)
        {
            string normalized = value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            if (normalized.Length == 0) return null;
            return normalized.Length <= maxChars ? normalized : normalized[..maxChars];
        }

        private static async Task<(DataObject? Snapshot, string? Text)> MaterializeClipboardWithRetryAsync()
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    IDataObject? original = Clipboard.GetDataObject();
                    if (original == null) return (new DataObject(), null);

                    var snapshot = new DataObject();
                    foreach (string format in original.GetFormats(autoConvert: false))
                    {
                        object? value = original.GetData(format, autoConvert: false);
                        if (value != null) snapshot.SetData(format, value);
                    }

                    return (snapshot, original.GetData(DataFormats.UnicodeText) as string);
                }
                catch (COMException) when (attempt < 7) { await Task.Delay(50); }
            }

            return (null, null);
        }
    }
}
