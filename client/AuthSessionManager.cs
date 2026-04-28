using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeExplainer
{
    internal sealed class AuthSessionManager
    {
        private readonly AuthApiClient _authApiClient;
        private readonly BrowserAuthCoordinator _browserAuthCoordinator;
        private readonly SecureTokenStore _tokenStore;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private StoredSessionState? _state;

        public AuthSessionManager(ClientConfig config)
        {
            _authApiClient = new AuthApiClient(config);
            _browserAuthCoordinator = new BrowserAuthCoordinator(config);
            _tokenStore = new SecureTokenStore();
            AuthRefreshSkewSeconds = config.AuthRefreshSkewSeconds;
        }

        private int AuthRefreshSkewSeconds { get; }

        public string? CurrentParticipantId
        {
            get
            {
                JwtTokenInfo? token = TryReadAccessToken();
                return token?.ParticipantId;
            }
        }

        public bool HasStoredSession => _state != null;

        public async Task<bool> TryRestoreSessionAsync()
        {
            _state = _tokenStore.Load();
            if (_state == null)
            {
                return false;
            }

            try
            {
                await EnsureValidAccessTokenAsync();
                return true;
            }
            catch
            {
                ClearLocalSession();
                return false;
            }
        }

        public async Task RedeemCodeAsync(string code)
        {
            AuthenticatedSessionResponse session = await _authApiClient.RedeemCodeAsync(code);
            Persist(session.AccessToken, session.RefreshToken);
        }

        public async Task SignInWithGoogleAsync()
        {
            BrowserAuthPreparationResponse preparation = await _authApiClient.PrepareGoogleLoginAsync(_browserAuthCoordinator.RedirectUri);
            BrowserAuthorizationResult authorization = await _browserAuthCoordinator.AuthorizeAsync(preparation.AuthorizationUrl);
            AuthenticatedSessionResponse session = await _authApiClient.CompleteGoogleLoginAsync(
                authorization.AuthorizationCode,
                authorization.State,
                preparation.FlowToken);
            Persist(session.AccessToken, session.RefreshToken);
        }

        public async Task SignInWithEmailPasswordAsync(string email, string password)
        {
            AuthenticatedSessionResponse session = await _authApiClient.LoginWithEmailPasswordAsync(email, password);
            Persist(session.AccessToken, session.RefreshToken);
        }

        public async Task RegisterWithEmailPasswordAsync(string email, string password, string displayName)
        {
            AuthenticatedSessionResponse session = await _authApiClient.RegisterWithEmailPasswordAsync(email, password, displayName);
            Persist(session.AccessToken, session.RefreshToken);
        }

        public async Task<AuthStateResponse> GetCurrentAuthStateAsync()
        {
            string accessToken = await EnsureValidAccessTokenAsync();
            return await _authApiClient.GetCurrentAuthStateAsync(accessToken);
        }

        public async Task LinkGoogleAsync()
        {
            string accessToken = await EnsureValidAccessTokenAsync();
            BrowserAuthPreparationResponse preparation = await _authApiClient.PrepareGoogleLinkAsync(_browserAuthCoordinator.RedirectUri, accessToken);
            BrowserAuthorizationResult authorization = await _browserAuthCoordinator.AuthorizeAsync(preparation.AuthorizationUrl);
            await _authApiClient.CompleteGoogleLinkAsync(
                authorization.AuthorizationCode,
                authorization.State,
                preparation.FlowToken,
                accessToken);
        }

        public async Task LinkRedeemCodeAsync(string code)
        {
            string accessToken = await EnsureValidAccessTokenAsync();
            await _authApiClient.LinkRedeemCodeAsync(code, accessToken);
        }

        public async Task LinkEmailPasswordAsync(string email, string password, string displayName)
        {
            string accessToken = await EnsureValidAccessTokenAsync();
            await _authApiClient.LinkEmailPasswordAsync(email, password, displayName, accessToken);
        }

        public async Task<string> EnsureValidAccessTokenAsync()
        {
            await _refreshLock.WaitAsync();
            try
            {
                if (_state == null)
                {
                    throw new SessionExpiredException("Sign-in is required.");
                }

                JwtTokenInfo token = ParseToken(_state.AccessToken);
                DateTimeOffset refreshThreshold = token.ExpiresAtUtc.AddSeconds(-AuthRefreshSkewSeconds);
                if (DateTimeOffset.UtcNow < refreshThreshold)
                {
                    return token.AccessToken;
                }

                TokenBundle refreshed = await _authApiClient.RefreshAsync(_state.RefreshToken);
                string refreshToken = string.IsNullOrWhiteSpace(refreshed.RefreshToken)
                    ? _state.RefreshToken
                    : refreshed.RefreshToken;
                Persist(refreshed.AccessToken, refreshToken);
                return refreshed.AccessToken;
            }
            catch (AuthApiException ex) when (ex.StatusCode is 400 or 401 or 409)
            {
                ClearLocalSession();
                throw new SessionExpiredException(ex.Message);
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        public async Task LogoutAsync()
        {
            StoredSessionState? snapshot = _state;
            ClearLocalSession();

            if (snapshot != null && !string.IsNullOrWhiteSpace(snapshot.RefreshToken))
            {
                try
                {
                    await _authApiClient.LogoutAsync(snapshot.RefreshToken);
                }
                catch
                {
                    // Best effort. Local session is already cleared.
                }
            }
        }

        public void ClearLocalSession()
        {
            _state = null;
            _tokenStore.Clear();
        }

        public string BuildRequestId(int requestSequence)
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 10);
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return $"req-{timestamp}-{requestSequence:D4}-{suffix}";
        }

        private void Persist(string accessToken, string refreshToken)
        {
            _state = new StoredSessionState
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                SavedAtUtc = DateTimeOffset.UtcNow
            };

            _tokenStore.Save(_state);
        }

        private JwtTokenInfo? TryReadAccessToken()
        {
            if (_state == null || string.IsNullOrWhiteSpace(_state.AccessToken))
            {
                return null;
            }

            try
            {
                return ParseToken(_state.AccessToken);
            }
            catch
            {
                return null;
            }
        }

        private static JwtTokenInfo ParseToken(string accessToken)
        {
            string[] parts = accessToken.Split('.');
            if (parts.Length < 2)
            {
                throw new InvalidOperationException("Access token format is invalid.");
            }

            string payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson)
                ?? new Dictionary<string, JsonElement>();

            string participantId = payload.TryGetValue("participant_id", out JsonElement participantElement)
                ? participantElement.ToString()
                : payload.TryGetValue("sub", out JsonElement subElement)
                    ? subElement.ToString()
                    : string.Empty;

            long expUnix = 0;
            if (payload.TryGetValue("exp", out JsonElement expElement))
            {
                if (expElement.ValueKind == JsonValueKind.Number)
                {
                    expElement.TryGetInt64(out expUnix);
                }
                else if (expElement.ValueKind == JsonValueKind.String)
                {
                    long.TryParse(expElement.GetString(), out expUnix);
                }
            }

            DateTimeOffset expiresAt = expUnix > 0
                ? DateTimeOffset.FromUnixTimeSeconds(expUnix)
                : DateTimeOffset.UtcNow.AddMinutes(5);

            return new JwtTokenInfo
            {
                AccessToken = accessToken,
                ParticipantId = participantId,
                ExpiresAtUtc = expiresAt
            };
        }

        private static byte[] Base64UrlDecode(string value)
        {
            string normalized = value.Replace('-', '+').Replace('_', '/');
            switch (normalized.Length % 4)
            {
                case 2:
                    normalized += "==";
                    break;
                case 3:
                    normalized += "=";
                    break;
            }

            return Convert.FromBase64String(normalized);
        }
    }

    internal sealed class SessionExpiredException : Exception
    {
        public SessionExpiredException(string message)
            : base(message)
        {
        }
    }
}
