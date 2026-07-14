# Auth And Session

Auth is built around one-time redeem codes. The idea is that pilot users do not need full OAuth; they enter a code, the backend maps it to a participant, and the client stores tokens securely for future requests.

The main client files are:

- `client/AuthApiClient.cs`
- `client/AuthSessionManager.cs`
- `client/SecureTokenStore.cs`
- `client/LoginWindow.xaml.cs`

The main backend files are:

- `backend/src/routes/auth.js`
- `backend/src/services/authService.js`
- `backend/src/middleware/auth.js`

## Why Redeem-code Auth

The project is pilot-oriented. Redeem-code auth is simpler than OAuth and fits internal testing:

- each tester gets a code
- the code can be used once
- the backend creates or links a participant
- the client receives access and refresh tokens
- request logs can be tied to a participant id

This avoids social login setup, consent screens, callback URLs, and provider configuration while still giving the backend a real identity signal.

## Client Session Flow

`AuthSessionManager` owns local session behavior.

On startup, `TryRestoreSessionAsync` loads a stored session from `SecureTokenStore`. If a session exists, it calls `EnsureValidAccessTokenAsync`. If token validation or refresh fails, the local session is cleared.

When the user enters a redeem code, `RedeemCodeAsync` calls the backend and persists the returned access and refresh tokens.

Before each protected backend request, `EnsureValidAccessTokenAsync` checks whether the access token is still valid. It parses the JWT payload locally, reads the `exp` timestamp, and refreshes early using `AuthRefreshSkewSeconds`.

The refresh path is protected by a `SemaphoreSlim`. That prevents multiple hotkey actions or feedback actions from trying to refresh the same session at the same time.

## Secure Local Storage

`SecureTokenStore` stores tokens locally using Windows DPAPI. DPAPI encrypts secrets for the current Windows user profile. That means another Windows account should not be able to read the saved refresh token directly.

The client does not put tokens in normal config files. Config controls where to connect; DPAPI storage controls who the user is.

## Backend Redeem Flow

`redeemCode` in `authService.js` performs the server-side flow:

1. Normalize and validate the code.
2. Ensure access-token signing is configured before mutating the database.
3. Load the redeem-code row from Supabase.
4. Reject missing or already-used codes.
5. Create a participant if needed.
6. Mark the redeem code as used.
7. Generate a refresh token.
8. Store only the refresh token hash.
9. Return a signed access token and the raw refresh token.

The backend stores the refresh token hash, not the refresh token itself. That means if the database table is read later, the raw token is not directly exposed.

## Access Tokens

Access tokens are JWTs signed with `ACCESS_TOKEN_SECRET` or the Supabase JWT secret fallback.

The payload includes:

- `sub`
- `participant_id`
- `type: access`
- expiration from `ACCESS_TOKEN_TTL`

Protected backend routes use `authenticateRequest`. It accepts bearer tokens from the `Authorization` header. For WebSockets, it can also accept `access_token` as a query parameter because some WebSocket clients are awkward about auth headers.

In local development, `SKIP_AUTH=true` bypasses auth and returns a fake local user. This is useful for dev smoke tests, but should not be enabled for hosted pilot deployment.

## Refresh Tokens

Refresh tokens are random high-entropy strings. They live longer than access tokens and are stored in the backend as SHA-256 hashes.

`refreshAccessToken` verifies:

- token exists
- token is not revoked
- token has not expired

Then it creates a new access token for the same participant.

The client keeps using the same refresh token after access-token refresh. Logout revokes it server-side and clears it locally.

## Logout

Client logout is intentionally best effort:

- clear local state first
- call backend logout to revoke refresh token
- ignore backend logout failures after local clear

That behavior protects the user experience. If the network fails during logout, the client still stops using the old session locally.

## Auth Middleware

`authMiddleware` wraps protected routes. It calls `authenticateRequest`, sets `user` on the Hono context, or returns an error response.

Protected routes include:

- `/api/explain`
- `/api/feedback`
- `/ws/stream`

The WebSocket route performs auth before upgrading the connection. That means an unauthenticated client does not get a long-lived socket.

## Important Config

The auth system depends on:

- `SKIP_AUTH`
- `ACCESS_TOKEN_SECRET`
- `ACCESS_TOKEN_TTL`
- `REFRESH_TOKEN_TTL_DAYS`
- `SUPABASE_URL`
- `SUPABASE_SERVICE_ROLE_KEY`
- auth table and column environment variables

`validateRuntimeConfig` warns when protected mode is enabled but required secrets are missing.

## Learning Takeaway

The auth design is not trying to be a general identity platform. It is a study/pilot identity layer. Its main job is to answer: which participant made this request, can they keep using the app, and can their refresh token be revoked?
