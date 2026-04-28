using System;
using System.Collections.Generic;
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

    public sealed class AuthenticatedSessionResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("user")]
        public AuthUserSummary? User { get; set; }

        [JsonPropertyName("methods")]
        public List<AuthMethodSummary> Methods { get; set; } = new();
    }

    public sealed class AuthStateResponse
    {
        [JsonPropertyName("user")]
        public AuthUserSummary? User { get; set; }

        [JsonPropertyName("methods")]
        public List<AuthMethodSummary> Methods { get; set; } = new();
    }

    public sealed class AuthUserSummary
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("email_verified")]
        public bool EmailVerified { get; set; }
    }

    public sealed class AuthMethodSummary
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("provider")]
        public string Provider { get; set; } = string.Empty;

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("linked_at")]
        public string? LinkedAt { get; set; }

        [JsonPropertyName("last_authenticated_at")]
        public string? LastAuthenticatedAt { get; set; }

        [JsonPropertyName("is_enabled")]
        public bool IsEnabled { get; set; } = true;
    }

    public sealed class BrowserAuthPreparationResponse
    {
        [JsonPropertyName("authorization_url")]
        public string AuthorizationUrl { get; set; } = string.Empty;

        [JsonPropertyName("flow_token")]
        public string FlowToken { get; set; } = string.Empty;
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

        [JsonPropertyName("code")]
        public string? Code { get; set; }
    }

    internal sealed class JwtTokenInfo
    {
        public string AccessToken { get; init; } = string.Empty;
        public string ParticipantId { get; init; } = string.Empty;
        public DateTimeOffset ExpiresAtUtc { get; init; }
    }

    public enum AuthWindowSubmissionKind
    {
        RedeemCodeLogin,
        GoogleLogin,
        EmailPasswordLogin,
        EmailPasswordRegister,
        GoogleLink,
        RedeemCodeLink,
        EmailPasswordLink
    }

    public sealed class AuthWindowSubmission
    {
        public AuthWindowSubmissionKind Kind { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
    }
}
