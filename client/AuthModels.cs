using System;
using System.Text.Json.Serialization;

namespace CodeExplainer
{
    internal sealed class TokenBundle
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;
    }

    internal sealed class StoredSessionState
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTimeOffset SavedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    internal sealed class AuthErrorResponse
    {
        [JsonPropertyName("error")]
        public string Error { get; set; } = string.Empty;
    }

    internal sealed class JwtTokenInfo
    {
        public string AccessToken { get; init; } = string.Empty;
        public string ParticipantId { get; init; } = string.Empty;
        public DateTimeOffset ExpiresAtUtc { get; init; }
    }
}
