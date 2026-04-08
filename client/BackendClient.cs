using System;
using System.Net.WebSockets;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeExplainer
{
    /// <summary>
    /// Communicates with the Node.js backend. HTTP POST for requests, WebSocket for streaming.
    /// </summary>
    public static class BackendClient
    {
        private static string WsUrl => "ws://localhost:3000";

        /// <summary>
        /// Sends an explain request and receives streamed tokens via WebSocket.
        /// </summary>
        public static async Task SendExplainRequest(
            string selectedText,
            string backgroundContext,
            string windowTitle,
            string processName,
            string environmentType,
            string selectedMethod,
            string backgroundMethod,
            bool isPartial,
            string? statusMessage,
            bool isUnsupported,
            Action<string>? onToken = null,
            Action<string>? onStatus = null,
            Action? onComplete = null,
            int? requestId = null,
            bool ocrUsed = false,
            float ocrConfidence = 0f)
        {
            string requestLabel = requestId?.ToString() ?? "-";
            var streamStopwatch = Stopwatch.StartNew();
            int tokenChunks = 0;
            int tokenChars = 0;

            try
            {
                using var ws = new ClientWebSocket();
                var wsUri = new Uri($"{WsUrl}/ws/stream");
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                RuntimeLog.Info(
                    "Backend",
                    $"req={requestLabel} stage=connecting url={wsUri} selected_chars={selectedText.Length} background_chars={backgroundContext.Length} env={environmentType} selected_method={selectedMethod} background_method={backgroundMethod} partial={isPartial} unsupported={isUnsupported}");
                await ws.ConnectAsync(wsUri, cts.Token);
                RuntimeLog.Info("Backend", $"req={requestLabel} stage=connected");

                var payload = new
                {
                    selected_text      = selectedText,
                    background_context = backgroundContext,
                    window_title       = windowTitle,
                    process_name       = processName,
                    environment_type   = environmentType,
                    selected_method    = selectedMethod,
                    background_method  = backgroundMethod,
                    is_partial         = isPartial,
                    is_unsupported     = isUnsupported,
                    status_message     = statusMessage ?? string.Empty,
                    ocr_used           = ocrUsed,
                    ocr_confidence     = ocrConfidence
                };

                string jsonPayload = JsonSerializer.Serialize(payload);
                var sendBuffer = Encoding.UTF8.GetBytes(jsonPayload);
                await ws.SendAsync(
                    new ArraySegment<byte>(sendBuffer),
                    WebSocketMessageType.Text,
                    true,
                    cts.Token);
                RuntimeLog.Info("Backend", $"req={requestLabel} stage=payload_sent");

                while (ws.State == WebSocketState.Open)
                {
                    string? message = await ReceiveFullMessageAsync(ws, cts.Token);
                    if (message == null)
                    {
                        break;
                    }

                    bool shouldContinue = HandleSocketMessage(
                        message,
                        onToken,
                        onStatus,
                        onComplete,
                        requestLabel,
                        ref tokenChunks,
                        ref tokenChars);
                    if (!shouldContinue)
                    {
                        break;
                    }
                }

                if (ws.State == WebSocketState.Open)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "client_done", CancellationToken.None);
                }

                streamStopwatch.Stop();
                RuntimeLog.Info(
                    "Backend",
                    $"req={requestLabel} stage=stream_finished token_chunks={tokenChunks} token_chars={tokenChars} duration_ms={streamStopwatch.ElapsedMilliseconds}");
            }
            catch (Exception ex)
            {
                streamStopwatch.Stop();
                RuntimeLog.Error("Backend", $"req={requestLabel} stage=error message={ex.Message}");
                System.Diagnostics.Debug.WriteLine($"BackendClient error: {ex.Message}");
                RunOnUiThread(() =>
                {
                    onToken?.Invoke($"\n[Connection error: {ex.Message}]");
                    onStatus?.Invoke("Connection error");
                    onComplete?.Invoke();
                });
            }
        }

        private static bool HandleSocketMessage(
            string message,
            Action<string>? onToken,
            Action<string>? onStatus,
            Action? onComplete,
            string requestLabel,
            ref int tokenChunks,
            ref int tokenChars)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(message);
                JsonElement root = document.RootElement;
                string type = root.GetProperty("type").GetString() ?? string.Empty;

                switch (type)
                {
                    case "meta":
                        if (root.TryGetProperty("label", out JsonElement label))
                        {
                            string streamLabel = label.GetString() ?? string.Empty;
                            RuntimeLog.Info("Backend", $"req={requestLabel} stage=meta label=\"{streamLabel}\"");
                            RunOnUiThread(() => onStatus?.Invoke(streamLabel));
                        }

                        return true;
                    case "token":
                        if (root.TryGetProperty("content", out JsonElement content))
                        {
                            string tokenText = content.GetString() ?? string.Empty;
                            tokenChunks++;
                            tokenChars += tokenText.Length;
                            if (tokenChunks <= 5 || tokenChunks % 25 == 0)
                            {
                                RuntimeLog.Info(
                                    "Backend",
                                    $"req={requestLabel} stage=token_chunk chunk={tokenChunks} chunk_chars={tokenText.Length} total_chars={tokenChars}");
                            }

                            RunOnUiThread(() => onToken?.Invoke(tokenText));
                        }

                        return true;
                    case "error":
                        if (root.TryGetProperty("message", out JsonElement error))
                        {
                            string errorMessage = error.GetString() ?? string.Empty;
                            RuntimeLog.Error("Backend", $"req={requestLabel} stage=stream_error message={errorMessage}");
                            RunOnUiThread(() =>
                            {
                                onToken?.Invoke($"\n[{errorMessage}]");
                                onStatus?.Invoke("Error");
                            });
                        }

                        RunOnUiThread(() => onComplete?.Invoke());
                        return false;
                    case "complete":
                        RuntimeLog.Info("Backend", $"req={requestLabel} stage=complete");
                        RunOnUiThread(() => onComplete?.Invoke());
                        return false;
                    default:
                        RuntimeLog.Warn("Backend", $"req={requestLabel} stage=unknown_message_type type={type}");
                        return true;
                }
            }
            catch
            {
                RuntimeLog.Warn("Backend", $"req={requestLabel} Received non-JSON message: {RuntimeLog.Preview(message)}");
                RunOnUiThread(() => onToken?.Invoke(message));

                return true;
            }
        }

        private static void RunOnUiThread(Action action)
        {
            if (System.Windows.Application.Current?.Dispatcher != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(action);
                return;
            }

            action();
        }

        private static async Task<string?> ReceiveFullMessageAsync(ClientWebSocket ws, CancellationToken cancellationToken)
        {
            var buffer = new ArraySegment<byte>(new byte[4096]);
            using var stream = new System.IO.MemoryStream();

            while (true)
            {
                WebSocketReceiveResult result = await ws.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                stream.Write(buffer.Array!, buffer.Offset, result.Count);
                if (result.EndOfMessage)
                {
                    break;
                }
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
