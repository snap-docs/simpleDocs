using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CodeExplainer
{
    internal sealed class AuthApiClient
    {
        private readonly HttpClient _httpClient;

        public AuthApiClient(ClientConfig config)
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(config.ApiBaseUrl + "/")
            };
        }

        public Task<TokenBundle> RedeemCodeAsync(string code)
        {
            return PostForTokensAsync("auth/redeem-code", new
            {
                code
            });
        }

        public async Task<string> RefreshAsync(string refreshToken)
        {
            TokenBundle response = await PostForTokensAsync("auth/refresh", new
            {
                refresh_token = refreshToken
            }, requireRefreshToken: false);

            return response.AccessToken;
        }

        public async Task LogoutAsync(string refreshToken)
        {
            await PostAsync("auth/logout", new
            {
                refresh_token = refreshToken
            });
        }

        private async Task<TokenBundle> PostForTokensAsync(string path, object payload, bool requireRefreshToken = true)
        {
            string json = await PostAsync(path, payload);
            var tokenBundle = JsonSerializer.Deserialize<TokenBundle>(json, JsonOptions());
            if (tokenBundle == null || string.IsNullOrWhiteSpace(tokenBundle.AccessToken))
            {
                throw new AuthApiException("Authentication response was incomplete.");
            }

            if (requireRefreshToken && string.IsNullOrWhiteSpace(tokenBundle.RefreshToken))
            {
                throw new AuthApiException("Authentication response did not include a refresh token.");
            }

            return tokenBundle;
        }

        private async Task<string> PostAsync(string path, object payload)
        {
            using var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using HttpResponseMessage response = await _httpClient.PostAsync(path, content);
            string body = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                return body;
            }

            string message = ExtractErrorMessage(body) ?? $"{(int)response.StatusCode} {response.ReasonPhrase}";
            throw new AuthApiException(message, (int)response.StatusCode);
        }

        private static string? ExtractErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                AuthErrorResponse? error = JsonSerializer.Deserialize<AuthErrorResponse>(body, JsonOptions());
                return string.IsNullOrWhiteSpace(error?.Error) ? null : error.Error;
            }
            catch
            {
                return body;
            }
        }

        private static JsonSerializerOptions JsonOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }
    }

    internal sealed class AuthApiException : Exception
    {
        public int? StatusCode { get; }

        public AuthApiException(string message, int? statusCode = null)
            : base(message)
        {
            StatusCode = statusCode;
        }
    }
}
