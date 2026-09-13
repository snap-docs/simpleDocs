using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CodeExplainer.Engine.Models;
using CodeExplainer.Engine.Strategies;

namespace CodeExplainer
{
    internal sealed class EditorExplainRequest
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("token")] public string? Token { get; set; }
        [JsonPropertyName("selected_text")] public string? SelectedText { get; set; }
        [JsonPropertyName("background_context")] public string? BackgroundContext { get; set; }
        [JsonPropertyName("document_name")] public string? DocumentName { get; set; }
        [JsonPropertyName("editor")] public string? Editor { get; set; }

        internal bool IsValid => Type == "explain"
            && !string.IsNullOrWhiteSpace(SelectedText) && SelectedText.Length <= 12000
            && BackgroundContext != null && BackgroundContext.Length <= 10000
            && BackgroundContext.Contains(SelectedText, StringComparison.Ordinal)
            && DocumentName is { Length: <= 400 } && Editor is "code" or "cursor";

        internal CaptureResult ToCaptureResult()
        {
            bool hasContext = ContextTextWindow.AddsContext(BackgroundContext!, SelectedText!);
            return new CaptureResult(SelectedText!, hasContext ? BackgroundContext! : "",
                DocumentName!, Editor!, EnvironmentType.IDE, CaptureMethod.EditorBridge,
                hasContext ? CaptureMethod.EditorBridge : CaptureMethod.None, !hasContext, false,
                "Selection received directly from the editor command.", usageContext: "editor_command");
        }
    }

    internal sealed class EditorRequestServer : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly string _pipe = $"simpleDocs-desktop-{Environment.ProcessId}-{Guid.NewGuid():N}";
        private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        private readonly string _manifest;
        private readonly Func<EditorExplainRequest, Task<bool>> _accept;
        private readonly Task _worker;

        public EditorRequestServer(Func<EditorExplainRequest, Task<bool>> accept, string? directory = null)
        {
            _accept = accept;
            directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodeExplainer", "desktop-bridge");
            Directory.CreateDirectory(directory);
            _manifest = Path.Combine(directory, _pipe + ".json");
            var firstListener = CreateListener();
            try
            {
                File.WriteAllText(_manifest, JsonSerializer.Serialize(new { pipe = _pipe, token = _token, protocol = 1 }));
                _worker = RunAsync(firstListener);
            }
            catch { firstListener.Dispose(); throw; }
        }

        private NamedPipeServerStream CreateListener() => new(_pipe, PipeDirection.InOut, 2,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        private async Task RunAsync(NamedPipeServerStream listener)
        {
            while (!_stop.IsCancellationRequested)
            {
                NamedPipeServerStream? next = null;
                using (listener)
                {
                    try
                    {
                        await listener.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                        // Keep discovery connectable while acknowledging this request.
                        next = CreateListener();
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                        timeout.CancelAfter(TimeSpan.FromSeconds(3));
                        string? json = await ReadRequestAsync(listener, timeout.Token).ConfigureAwait(false);
                        var request = json == null ? null : JsonSerializer.Deserialize<EditorExplainRequest>(json);
                        string status = "invalid";
                        if (request?.Token == _token && request.IsValid)
                            status = await _accept(request).ConfigureAwait(false) ? "accepted" : "busy";
                        byte[] response = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { status }) + "\n");
                        await listener.WriteAsync(response, timeout.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        if (!_stop.IsCancellationRequested)
                            RuntimeLog.Warn("EditorCommand", $"Request unavailable: {ex.GetType().Name}");
                    }
                }
                if (_stop.IsCancellationRequested) { next?.Dispose(); break; }
                try { listener = next ?? CreateListener(); }
                catch (Exception ex)
                {
                    RuntimeLog.Error("EditorCommand", $"Listener stopped: {ex.GetType().Name}");
                    break;
                }
            }
        }

        private static async Task<string?> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
        {
            using var data = new MemoryStream();
            byte[] buffer = new byte[4096];
            while (data.Length <= 131072)
            {
                int count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (count == 0) return null;
                int newline = Array.IndexOf(buffer, (byte)'\n', 0, count);
                data.Write(buffer, 0, newline < 0 ? count : newline);
                if (data.Length > 131072) return null;
                if (newline >= 0) return Encoding.UTF8.GetString(data.ToArray());
            }
            return null;
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { File.Delete(_manifest); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            // Do not block the UI dispatcher while a queued command is being acknowledged.
        }
    }
}
