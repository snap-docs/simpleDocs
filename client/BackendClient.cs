using System;
using System.Net.WebSockets;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Net;

namespace CodeExplainer
{
    /// <summary>
    /// Communicates with the Node.js backend. HTTP POST for requests, WebSocket for streaming.
    /// </summary>
    internal static class BackendClient
    {
        private static ClientConfig _config = new ClientConfig();

        public static void Configure(ClientConfig config)
        {
            _config = config ?? new ClientConfig();
        }

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
            string accessToken,
            string requestId,
            string usageContext,
            Action<string>? onToken = null,
            Action<string>? onStatus = null,
            Action? onComplete = null,
            bool ocrUsed = false,
            float ocrConfidence = 0f)
        {
            var streamStopwatch = Stopwatch.StartNew();
            int tokenChunks = 0;
            int tokenChars = 0;
            string cleanSelectedText = TextSanitizer.SanitizePayloadText(selectedText, 5000);
            string cleanBackgroundContext = TextSanitizer.SanitizePayloadText(backgroundContext, 12000);
            string cleanWindowTitle = TextSanitizer.SanitizePayloadText(windowTitle, 400);
            string cleanProcessName = TextSanitizer.SanitizePayloadText(processName, 100);
            string cleanStatusMessage = TextSanitizer.SanitizePayloadText(statusMessage, 240);

            try
            {
                using var ws = await ConnectWithRetryAsync(accessToken, requestId, cleanSelectedText.Length, cleanBackgroundContext.Length, environmentType, selectedMethod, backgroundMethod, isPartial, isUnsupported);
                using var streamCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

                var payload = new
                {
                    request_id         = requestId,
                    usage_context      = usageContext,
                    selected_text      = cleanSelectedText,
                    background_context = cleanBackgroundContext,
                    window_title       = cleanWindowTitle,
                    process_name       = cleanProcessName,
                    environment_type   = environmentType,
                    selected_method    = selectedMethod,
                    background_method  = backgroundMethod,
                    is_partial         = isPartial,
                    is_unsupported     = isUnsupported,
                    status_message     = cleanStatusMessage,
                    ocr_used           = ocrUsed,
                    ocr_confidence     = ocrConfidence
                };

                string jsonPayload = JsonSerializer.Serialize(payload);
                var sendBuffer = Encoding.UTF8.GetBytes(jsonPayload);
                await ws.SendAsync(
                    new ArraySegment<byte>(sendBuffer),
                    WebSocketMessageType.Text,
                    true,
                    streamCts.Token);
                RuntimeLog.Info("Backend", $"req={requestId} stage=payload_sent");

                while (ws.State == WebSocketState.Open)
                {
                    string? message = await ReceiveFullMessageAsync(ws, streamCts.Token);
                    if (message == null)
                    {
                        break;
                    }

                    bool shouldContinue = HandleSocketMessage(
                        message,
                        onToken,
                        onStatus,
                        onComplete,
                        requestId,
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
                    $"req={requestId} stage=stream_finished token_chunks={tokenChunks} token_chars={tokenChars} duration_ms={streamStopwatch.ElapsedMilliseconds}");
            }
            catch (Exception ex)
            {
                streamStopwatch.Stop();
                RuntimeLog.Error("Backend", $"req={requestId} stage=error message={ex.Message}");
                System.Diagnostics.Debug.WriteLine($"BackendClient error: {ex.Message}");
                RunOnUiThread(() =>
                {
                    onToken?.Invoke($"\n[Connection error: {ex.Message}]");
                    onStatus?.Invoke("Connection error");
                    onComplete?.Invoke();
                });
            }
        }

        public static async Task SendFeedbackAsync(string requestId, string reaction, string accessToken)
        {
            string cleanRequestId = TextSanitizer.SanitizePayloadText(requestId, 120);
            string cleanReaction = reaction?.Trim().ToLowerInvariant() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cleanRequestId))
            {
                throw new InvalidOperationException("Feedback request id is missing.");
            }

            if (cleanReaction != "up" && cleanReaction != "down")
            {
                throw new InvalidOperationException("Feedback reaction must be up or down.");
            }

            using var client = new HttpClient
            {
                BaseAddress = new Uri(_config.ApiBaseUrl + "/")
            };

            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            using var content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    request_id = cleanRequestId,
                    reaction = cleanReaction
                }),
                Encoding.UTF8,
                "application/json");

            using HttpResponseMessage response = await client.PostAsync("api/feedback", content);
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Feedback request failed: {(int)response.StatusCode} {body}");
            }

            RuntimeLog.Info("Feedback", $"request_id={cleanRequestId} reaction={cleanReaction} stored=true");
        }

        private static async Task<ClientWebSocket> ConnectWithRetryAsync(
            string accessToken,
            string requestId,
            int selectedChars,
            int backgroundChars,
            string environmentType,
            string selectedMethod,
            string backgroundMethod,
            bool isPartial,
            bool isUnsupported)
        {
            Exception? lastError = null;
            Uri wsUri = BuildWebSocketUri(accessToken);

            for (int attempt = 1; attempt <= Math.Max(1, _config.WebSocketRetryCount); attempt++)
            {
                var ws = new ClientWebSocket();
                if (!string.IsNullOrWhiteSpace(accessToken))
                {
                    ws.Options.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_config.WebSocketConnectTimeoutSeconds));
                try
                {
                    RuntimeLog.Info(
                        "Backend",
                        $"req={requestId} stage=connecting attempt={attempt} url={MaskAccessToken(wsUri)} selected_chars={selectedChars} background_chars={backgroundChars} env={environmentType} selected_method={selectedMethod} background_method={backgroundMethod} partial={isPartial} unsupported={isUnsupported} token_present={!string.IsNullOrWhiteSpace(accessToken)}");
                    await ws.ConnectAsync(wsUri, cts.Token);
                    RuntimeLog.Info("Backend", $"req={requestId} stage=connected attempt={attempt}");
                    return ws;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    ws.Dispose();
                    RuntimeLog.Warn("Backend", $"req={requestId} stage=connect_retry attempt={attempt} message={ex.Message}");
                    if (attempt < Math.Max(1, _config.WebSocketRetryCount))
                    {
                        int delayMs = _config.WebSocketRetryBaseDelayMs * attempt;
                        await Task.Delay(delayMs);
                    }
                }
            }

            throw new HttpRequestException($"Unable to connect to backend WebSocket. {lastError?.Message}", lastError);
        }

        private static Uri BuildWebSocketUri(string accessToken)
        {
            string baseUrl = _config.WsBaseUrl?.TrimEnd('/') ?? string.Empty;
            string fullUrl = $"{baseUrl}/ws/stream";

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return new Uri(fullUrl);
            }

            string separator = fullUrl.Contains("?") ? "&" : "?";
            string encodedToken = WebUtility.UrlEncode(accessToken);
            return new Uri($"{fullUrl}{separator}access_token={encodedToken}");
        }

        private static string MaskAccessToken(Uri uri)
        {
            string value = uri.ToString();
            int tokenIndex = value.IndexOf("access_token=", StringComparison.OrdinalIgnoreCase);
            if (tokenIndex < 0)
            {
                return value;
            }

            int tokenValueStart = tokenIndex + "access_token=".Length;
            int tokenValueEnd = value.IndexOf('&', tokenValueStart);
            if (tokenValueEnd < 0)
            {
                tokenValueEnd = value.Length;
            }

            return value.Substring(0, tokenValueStart) + "<redacted>" + value.Substring(tokenValueEnd);
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
