using System;
using System.Diagnostics;
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
        private static readonly System.Text.RegularExpressions.Regex PipePattern = new(
            "^simpleDocs-editor-([0-9]+)-[a-f0-9]{32}$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

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
            using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(1400));
            try
            {
                for (int scan = 0; scan < 2; scan++)
                {
                    foreach (var file in new DirectoryInfo(directory).EnumerateFiles("simpleDocs-editor-*.json")
                        .OrderByDescending(f => f.LastWriteTimeUtc).Take(16))
                    {
                        if (budget.IsCancellationRequested || CaptureScope.Current?.IsValid == false) return null;
                        if (file.Length > 2048) continue;
                        try
                        {
                            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(file.FullName, budget.Token));
                            string pipe = manifest.RootElement.GetProperty("pipe").GetString() ?? "";
                            string token = manifest.RootElement.GetProperty("token").GetString() ?? "";
                            var pipeMatch = PipePattern.Match(pipe);
                            if (!pipeMatch.Success
                                || !System.Text.RegularExpressions.Regex.IsMatch(token, "^[a-f0-9]{64}$")) continue;

                            if (!int.TryParse(pipeMatch.Groups[1].Value, System.Globalization.NumberStyles.None,
                                System.Globalization.CultureInfo.InvariantCulture, out int bridgeProcessId)) continue;
                            if (!IsProcessRunning(bridgeProcessId))
                            {
                                DeleteStaleManifest(file.FullName);
                                continue;
                            }

                            uint? foregroundProcessId = CaptureScope.Current?.Window.ProcessId;
                            uint? ownerProcessId = manifest.RootElement.TryGetProperty("owner_pid", out JsonElement ownerPidElement)
                                && ownerPidElement.TryGetUInt32(out uint ownerPid) && ownerPid > 0 ? ownerPid : null;
                            if (!OwnerMatches(ownerProcessId, foregroundProcessId)) continue;

                            using var connection = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
                                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                            await connection.ConnectAsync(200, budget.Token);
                            byte[] request = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "capture", token, selected_text = selection,
                                window_pid = foregroundProcessId }) + "\n");
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

                    if (scan == 0 && selection == null && !budget.IsCancellationRequested)
                        await Task.Delay(250, budget.Token);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException) { }
            return null;
        }

        internal static bool OwnerMatches(uint? ownerProcessId, uint? foregroundProcessId) =>
            !ownerProcessId.HasValue || !foregroundProcessId.HasValue || ownerProcessId == foregroundProcessId;

        private static bool IsProcessRunning(int processId)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return false; }
            catch (NotSupportedException) { return false; }
        }

        private static void DeleteStaleManifest(string path)
        {
            try
            {
                File.Delete(path);
                RuntimeLog.Info("EditorBridge", $"Removed stale bridge manifest: {Path.GetFileName(path)}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
