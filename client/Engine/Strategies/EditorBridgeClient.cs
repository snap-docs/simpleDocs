using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace CodeExplainer.Engine.Strategies
{
    internal static class EditorBridgeClient
    {
        internal sealed class Snapshot
        {
            [JsonPropertyName("selected_text")] public string? SelectedText { get; set; }
            [JsonPropertyName("background_context")] public string? BackgroundContext { get; set; }
        }

        internal static bool IsValid(Snapshot? snapshot, string? selectedText) =>
            !string.IsNullOrWhiteSpace(snapshot?.SelectedText) && snapshot.BackgroundContext != null
            && snapshot.SelectedText.Length <= 5000 && snapshot.BackgroundContext.Length <= 10000
            && (selectedText == null || Normalize(snapshot.SelectedText) == Normalize(selectedText))
            && snapshot.BackgroundContext.Contains(snapshot.SelectedText, StringComparison.Ordinal)
            && (selectedText == null || ContextTextWindow.AddsContext(snapshot.BackgroundContext, snapshot.SelectedText));

        private static string Normalize(string value) => value.Replace("\r\n", "\n").Trim();

        public static async Task<Snapshot?> TryCaptureAsync(string? selection, string? directory = null)
        {
            if (selection != null && string.IsNullOrWhiteSpace(selection)) return null;
            directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodeExplainer", "editor-bridge");
            if (!Directory.Exists(directory)) { RuntimeLog.Info("EditorBridge", "No active editor bridge directory."); return null; }
            using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(650));
            try
            {
                foreach (var file in new DirectoryInfo(directory).EnumerateFiles("simpleDocs-editor-*.json")
                    .OrderByDescending(f => f.LastWriteTimeUtc).Take(8))
                {
                    if (budget.IsCancellationRequested || CaptureScope.Current?.IsValid == false) return null;
                    if (file.Length > 2048) continue;
                    try
                    {
                        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(file.FullName, budget.Token));
                        if (selection == null && (!manifest.RootElement.TryGetProperty("protocol", out var protocol)
                            || !protocol.TryGetInt32(out int version) || version < 2))
                        {
                            RuntimeLog.Warn("EditorBridge", "An older extension is still loaded. Reload the editor and use simpleDocs: Explain Selection.");
                            continue;
                        }
                        string pipe = manifest.RootElement.GetProperty("pipe").GetString() ?? "";
                        string token = manifest.RootElement.GetProperty("token").GetString() ?? "";
                        if (!System.Text.RegularExpressions.Regex.IsMatch(pipe, "^simpleDocs-editor-[0-9]+-[a-f0-9]{32}$")
                            || !System.Text.RegularExpressions.Regex.IsMatch(token, "^[a-f0-9]{64}$")) continue;
                        using var connection = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
                            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                        await connection.ConnectAsync(100, budget.Token);
                        byte[] request = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "capture", token, selected_text = selection,
                            window_pid = CaptureScope.Current?.Window.ProcessId }) + "\n");
                        await connection.WriteAsync(request, budget.Token);
                        using var response = new MemoryStream();
                        byte[] chunk = new byte[4096];
                        while (response.Length <= 131072)
                        {
                            int count = await connection.ReadAsync(chunk, budget.Token);
                            if (count == 0) break;
                            int newline = Array.IndexOf(chunk, (byte)'\n', 0, count);
                            response.Write(chunk, 0, newline < 0 ? count : newline);
                            if (newline >= 0) break;
                        }
                        if (response.Length > 131072) continue;
                        var snapshot = JsonSerializer.Deserialize<Snapshot>(response.ToArray());
                        if (CaptureScope.Current?.IsValid != false && IsValid(snapshot, selection)) return snapshot;
                        RuntimeLog.Info("EditorBridge", "Bridge responded without a valid focused selection.");
                    }
                    catch (Exception ex) when (ex is IOException or TimeoutException or JsonException
                        or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
                    { RuntimeLog.Info("EditorBridge", $"Bridge unavailable: {ex.GetType().Name}."); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return null;
        }
    }
}
