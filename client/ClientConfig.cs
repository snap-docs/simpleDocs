using System;
using System.IO;
using System.Text.Json;

namespace CodeExplainer
{
    internal sealed class ClientConfig
    {
        public string EnvironmentName { get; init; } = "Development";
        public string ApiBaseUrl { get; init; } = "http://localhost:3000";
        public string WsBaseUrl { get; init; } = "ws://localhost:3000";
        public bool AuthEnabled { get; init; } = true;
        public int AuthRefreshSkewSeconds { get; init; } = 60;
        public int AuthBrowserCallbackPort { get; init; } = 48152;
        public int WebSocketConnectTimeoutSeconds { get; init; } = 30;
        public int WebSocketRetryCount { get; init; } = 3;
        public int WebSocketRetryBaseDelayMs { get; init; } = 500;
        public bool EnableVisionPipeline { get; init; } = true;
        public int VisionMaxImageBytes { get; init; } = 819200;
        public int VisionCaptureTimeoutMs { get; init; } = 800;
        public string? VisionDebugDirectory { get; init; }

        public static ClientConfig Load()
        {
            var settings = new ConfigFileModel();
            ConfigFileModel baseSettings = ReadConfigFile("appsettings.json");
            Merge(settings, baseSettings);

            string environmentName =
                Environment.GetEnvironmentVariable("CODE_EXPLAINER_ENV")?.Trim()
                ?? baseSettings.Environment?.Trim()
                ?? "Development";

            Merge(settings, ReadConfigFile($"appsettings.{environmentName}.json"));

            string apiBaseUrl = GetEnvOverride("CODE_EXPLAINER_API_BASE_URL")
                ?? settings.Backend.ApiBaseUrl
                ?? "http://localhost:3000";

            string wsBaseUrl = GetEnvOverride("CODE_EXPLAINER_WS_BASE_URL")
                ?? settings.Backend.WsBaseUrl
                ?? DeriveWebSocketUrl(apiBaseUrl);

            return new ClientConfig
            {
                EnvironmentName = environmentName,
                ApiBaseUrl = TrimTrailingSlash(apiBaseUrl),
                WsBaseUrl = TrimTrailingSlash(wsBaseUrl),
                AuthEnabled = ParseBoolOverride("CODE_EXPLAINER_AUTH_ENABLED", settings.Auth.Enabled, true),
                AuthRefreshSkewSeconds = ParseIntOverride("CODE_EXPLAINER_AUTH_REFRESH_SKEW_SECONDS", settings.Auth.RefreshSkewSeconds, 60),
                AuthBrowserCallbackPort = ParseAuthBrowserCallbackPort(settings),
                WebSocketConnectTimeoutSeconds = ParseIntOverride("CODE_EXPLAINER_WS_CONNECT_TIMEOUT_SECONDS", settings.Streaming.ConnectTimeoutSeconds, 30),
                WebSocketRetryCount = ParseIntOverride("CODE_EXPLAINER_WS_RETRY_COUNT", settings.Streaming.RetryCount, 3),
                WebSocketRetryBaseDelayMs = ParseIntOverride("CODE_EXPLAINER_WS_RETRY_BASE_DELAY_MS", settings.Streaming.RetryBaseDelayMs, 500),
                EnableVisionPipeline = ParseBoolOverride("CODE_EXPLAINER_ENABLE_VISION_PIPELINE", settings.Vision.Enabled, true),
                VisionMaxImageBytes = ParseIntOverride("CODE_EXPLAINER_VISION_MAX_IMAGE_BYTES", settings.Vision.MaxImageBytes, 819200),
                VisionCaptureTimeoutMs = ParseIntOverride("CODE_EXPLAINER_VISION_CAPTURE_TIMEOUT_MS", settings.Vision.CaptureTimeoutMs, 800),
                VisionDebugDirectory = GetEnvOverride("CODE_EXPLAINER_VISION_DEBUG_DIR")
            };
        }

        private static ConfigFileModel ReadConfigFile(string fileName)
        {
            string path = Path.Combine(AppContext.BaseDirectory, fileName);
            if (!File.Exists(path))
            {
                return new ConfigFileModel();
            }

            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<ConfigFileModel>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new ConfigFileModel();
            }
            catch
            {
                return new ConfigFileModel();
            }
        }

        private static void Merge(ConfigFileModel target, ConfigFileModel source)
        {
            if (!string.IsNullOrWhiteSpace(source.Environment))
            {
                target.Environment = source.Environment;
            }

            target.Backend.ApiBaseUrl = source.Backend.ApiBaseUrl ?? target.Backend.ApiBaseUrl;
            target.Backend.WsBaseUrl = source.Backend.WsBaseUrl ?? target.Backend.WsBaseUrl;
            target.Auth.Enabled = source.Auth.Enabled ?? target.Auth.Enabled;
            target.Auth.RefreshSkewSeconds = source.Auth.RefreshSkewSeconds ?? target.Auth.RefreshSkewSeconds;
            target.Auth.BrowserCallbackPort = source.Auth.BrowserCallbackPort ?? target.Auth.BrowserCallbackPort;
            target.Auth.GoogleRedirectPort = source.Auth.GoogleRedirectPort ?? target.Auth.GoogleRedirectPort;
            target.Streaming.ConnectTimeoutSeconds = source.Streaming.ConnectTimeoutSeconds ?? target.Streaming.ConnectTimeoutSeconds;
            target.Streaming.RetryCount = source.Streaming.RetryCount ?? target.Streaming.RetryCount;
            target.Streaming.RetryBaseDelayMs = source.Streaming.RetryBaseDelayMs ?? target.Streaming.RetryBaseDelayMs;
            target.Vision.Enabled = source.Vision.Enabled ?? target.Vision.Enabled;
            target.Vision.MaxImageBytes = source.Vision.MaxImageBytes ?? target.Vision.MaxImageBytes;
            target.Vision.CaptureTimeoutMs = source.Vision.CaptureTimeoutMs ?? target.Vision.CaptureTimeoutMs;
        }

        private static string? GetEnvOverride(string name)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static bool ParseBoolOverride(string name, bool? fileValue, bool fallback)
        {
            string? raw = GetEnvOverride(name);
            if (raw != null)
            {
                if (bool.TryParse(raw, out bool boolValue))
                {
                    return boolValue;
                }

                if (raw == "1")
                {
                    return true;
                }

                if (raw == "0")
                {
                    return false;
                }
            }

            if (fileValue.HasValue)
            {
                return fileValue.Value;
            }

            return fallback;
        }

        private static int ParseAuthBrowserCallbackPort(ConfigFileModel settings)
        {
            string? envValue = GetEnvOverride("CODE_EXPLAINER_AUTH_BROWSER_CALLBACK_PORT")
                ?? GetEnvOverride("CODE_EXPLAINER_GOOGLE_REDIRECT_PORT");

            if (envValue != null && int.TryParse(envValue, out int parsedFromEnv) && parsedFromEnv > 0)
            {
                return parsedFromEnv;
            }

            if (settings.Auth.BrowserCallbackPort.HasValue && settings.Auth.BrowserCallbackPort.Value > 0)
            {
                return settings.Auth.BrowserCallbackPort.Value;
            }

            if (settings.Auth.GoogleRedirectPort.HasValue && settings.Auth.GoogleRedirectPort.Value > 0)
            {
                return settings.Auth.GoogleRedirectPort.Value;
            }

            return 48152;
        }

        private static int ParseIntOverride(string name, int? fileValue, int fallback)
        {
            string? raw = GetEnvOverride(name);
            if (raw != null && int.TryParse(raw, out int parsedFromEnv) && parsedFromEnv > 0)
            {
                return parsedFromEnv;
            }

            if (fileValue.HasValue && fileValue.Value > 0)
            {
                return fileValue.Value;
            }

            return fallback;
        }

        private static string DeriveWebSocketUrl(string apiBaseUrl)
        {
            if (apiBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return "wss://" + apiBaseUrl.Substring("https://".Length);
            }

            if (apiBaseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return "ws://" + apiBaseUrl.Substring("http://".Length);
            }

            return apiBaseUrl;
        }

        private static string TrimTrailingSlash(string value)
        {
            return value.Trim().TrimEnd('/');
        }

        private sealed class ConfigFileModel
        {
            public string? Environment { get; set; }
            public BackendSection Backend { get; set; } = new();
            public AuthSection Auth { get; set; } = new();
            public StreamingSection Streaming { get; set; } = new();
            public VisionSection Vision { get; set; } = new();
        }

        private sealed class BackendSection
        {
            public string? ApiBaseUrl { get; set; }
            public string? WsBaseUrl { get; set; }
        }

        private sealed class AuthSection
        {
            public bool? Enabled { get; set; }
            public int? RefreshSkewSeconds { get; set; }
            public int? BrowserCallbackPort { get; set; }
            public int? GoogleRedirectPort { get; set; }
        }

        private sealed class StreamingSection
        {
            public int? ConnectTimeoutSeconds { get; set; }
            public int? RetryCount { get; set; }
            public int? RetryBaseDelayMs { get; set; }
        }

        private sealed class VisionSection
        {
            public bool? Enabled { get; set; }
            public int? MaxImageBytes { get; set; }
            public int? CaptureTimeoutMs { get; set; }
        }
    }
}
