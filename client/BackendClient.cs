using System;
using System.Net.Http;
using System.Net.WebSockets;
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
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        // Configure these for your environment
        private static string BaseUrl => "http://localhost:3000";
        private static string WsUrl => "ws://localhost:3000";

        /// <summary>
        /// Sends an explain request and receives streamed tokens via WebSocket.
        /// </summary>
        public static async Task SendExplainRequest(
            string selectedText,
            string fullContext,
            string appType,
            Action<string>? onToken = null,
            Action? onComplete = null)
        {
            try
            {
                // Step 1: Connect WebSocket first to receive streamed response
                using var ws = new ClientWebSocket();
                var wsUri = new Uri($"{WsUrl}/ws/stream");

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                await ws.ConnectAsync(wsUri, cts.Token);

                // Step 2: Send request payload over WebSocket
                var payload = new
                {
                    selected_text = selectedText,
                    full_context = fullContext,
                    app_type = appType
                };

                string jsonPayload = JsonSerializer.Serialize(payload);
                var sendBuffer = Encoding.UTF8.GetBytes(jsonPayload);
                await ws.SendAsync(
                    new ArraySegment<byte>(sendBuffer),
                    WebSocketMessageType.Text,
                    true,
                    cts.Token);

                // Step 3: Receive streaming tokens
                var receiveBuffer = new byte[4096];
                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(
                        new ArraySegment<byte>(receiveBuffer),
                        cts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    string message = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);

                    if (message == "[DONE]")
                    {
                        onComplete?.Invoke();
                        break;
                    }

                    // Dispatch token to UI thread
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        onToken?.Invoke(message);
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BackendClient error: {ex.Message}");
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    onToken?.Invoke($"\n[Connection error: {ex.Message}]");
                    onComplete?.Invoke();
                });
            }
        }
    }
}
