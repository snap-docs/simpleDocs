using System;
using System.Net.Http;
using System.Net.Http.Headers;
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

        public Task<AuthenticatedSessionResponse> RedeemCodeAsync(string code)
        {
            return PostForSessionAsync("auth/providers/redeem-code/login", new
            {
                code
            });
        }

        public Task<BrowserAuthPreparationResponse> PrepareGoogleLoginAsync(string redirectUri)
        {
            return PostJsonAsync<BrowserAuthPreparationResponse>("auth/providers/google/login/prepare", new
            {
                redirect_uri = redirectUri
            });
        }

        public Task<AuthenticatedSessionResponse> CompleteGoogleLoginAsync(string code, string state, string flowToken)
        {
            return PostForSessionAsync("auth/providers/google/login/complete", new
            {
                code,
                state,
                flow_token = flowToken
            });
        }

        public Task<AuthenticatedSessionResponse> LoginWithEmailPasswordAsync(string email, string password)
        {
            return PostForSessionAsync("auth/providers/email-password/login", new
            {
                email,
                password
            });
        }

        public Task<AuthenticatedSessionResponse> RegisterWithEmailPasswordAsync(string email, string password, string displayName)
        {
            return PostForSessionAsync("auth/providers/email-password/register", new
            {
                email,
                password,
                display_name = displayName
            });
        }

        public Task<BrowserAuthPreparationResponse> PrepareGoogleLinkAsync(string redirectUri, string accessToken)
        {
            return PostJsonAsync<BrowserAuthPreparationResponse>("auth/providers/google/link/prepare", new
            {
                redirect_uri = redirectUri
            }, accessToken);
        }

        public Task<AuthStateResponse> CompleteGoogleLinkAsync(string code, string state, string flowToken, string accessToken)
        {
            return PostJsonAsync<AuthStateResponse>("auth/providers/google/link/complete", new
            {
                code,
                state,
                flow_token = flowToken
            }, accessToken);
        }

        public Task<AuthStateResponse> LinkRedeemCodeAsync(string code, string accessToken)
        {
            return PostJsonAsync<AuthStateResponse>("auth/providers/redeem-code/link", new
            {
                code
            }, accessToken);
        }

        public Task<AuthStateResponse> LinkEmailPasswordAsync(string email, string password, string displayName, string accessToken)
        {
            return PostJsonAsync<AuthStateResponse>("auth/providers/email-password/link", new
            {
                email,
                password,
                display_name = displayName
            }, accessToken);
        }

        public Task<AuthStateResponse> GetCurrentAuthStateAsync(string accessToken)
        {
            return GetJsonAsync<AuthStateResponse>("auth/me", accessToken);
        }

        public Task<TokenBundle> RefreshAsync(string refreshToken)
        {
            return PostForTokensAsync("auth/refresh", new
            {
                refresh_token = refreshToken
            }, requireRefreshToken: false);
        }

        public async Task LogoutAsync(string refreshToken)
        {
            await PostAsync("auth/logout", new
            {
                refresh_token = refreshToken
            });
        }

        private async Task<AuthenticatedSessionResponse> PostForSessionAsync(string path, object payload, string? accessToken = null)
        {
            AuthenticatedSessionResponse response = await PostJsonAsync<AuthenticatedSessionResponse>(path, payload, accessToken);
            if (response == null || string.IsNullOrWhiteSpace(response.AccessToken))
            {
                throw new AuthApiException("Authentication response was incomplete.");
            }

            if (string.IsNullOrWhiteSpace(response.RefreshToken))
            {
                throw new AuthApiException("Authentication response did not include a refresh token.");
            }

            return response;
        }

        private async Task<TokenBundle> PostForTokensAsync(string path, object payload, bool requireRefreshToken = true)
        {
            TokenBundle tokenBundle = await PostJsonAsync<TokenBundle>(path, payload);
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

        private async Task<T> PostJsonAsync<T>(string path, object payload, string? accessToken = null)
        {
            string json = await PostAsync(path, payload, accessToken);
            T? data = JsonSerializer.Deserialize<T>(json, JsonOptions());
            if (data == null)
            {
                throw new AuthApiException("The authentication service returned an invalid response.");
            }

            return data;
        }

        private async Task<T> GetJsonAsync<T>(string path, string accessToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            AttachBearerToken(request, accessToken);
            string json = await SendAsync(request);
            T? data = JsonSerializer.Deserialize<T>(json, JsonOptions());
            if (data == null)
            {
                throw new AuthApiException("The authentication service returned an invalid response.");
            }

            return data;
        }

        private async Task<string> PostAsync(string path, object payload, string? accessToken = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json")
            };
            AttachBearerToken(request, accessToken);
            return await SendAsync(request);
        }

        private async Task<string> SendAsync(HttpRequestMessage request)
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                return body;
            }

            string message = ExtractErrorMessage(body) ?? $"{(int)response.StatusCode} {response.ReasonPhrase}";
            throw new AuthApiException(message, (int)response.StatusCode);
        }

        private static void AttachBearerToken(HttpRequestMessage request, string? accessToken)
        {
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
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
