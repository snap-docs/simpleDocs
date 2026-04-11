# Intelligent Context Engine & Code Explainer

This repository contains a Windows-first OS-level assistant that explains highlighted code, browser text, and terminal output inside a floating overlay without forcing the user to switch windows.

The core architecture remains intentionally unchanged:
- C# / .NET 8 / WPF desktop client
- Native Windows capture pipeline using UIA, MSAA, clipboard fallback, console APIs, and OCR
- Node.js backend with Hono
- WebSocket streaming for live responses
- Hosted Supabase/Postgres for auth and study logging

## Current Product Status

The project is now in late pilot-verification stage.

Completed now:
- native capture pipeline is in place
- overlay streaming flow is in place
- short overlay-focused explanation style is in place
- thumbs up / thumbs down feedback is in place
- one-time redeem-code auth is implemented
- Windows secure token storage is implemented
- authenticated WebSocket flow is implemented
- request/session logging path to hosted DB is implemented
- Azure App Service backend is deployed
- hosted `/api/health` is live and returning `ok`
- hosted redeem-code login is working
- hosted refresh and logout flow are working
- hosted authenticated WebSocket explain flow is working
- hosted DB row creation is working for `participants`, `refresh_tokens`, `sessions`, and `request_logs`
- client staging/production config now points to the hosted Azure backend
- production client publish works
- tester bundle build works
- sign-in UI has been polished for production use

Current remaining validation:
- one full manual redeem-code login still needs to be completed from the packaged WPF sign-in window
- one full manual hotkey-triggered explanation still needs to be completed from the packaged WPF client
- the tester bundle still needs one clean-machine validation outside the development machine

## High-Level Runtime Flow

1. User launches the Windows client.
2. The client restores the stored session or prompts for a redeem code.
3. The client stores tokens securely on Windows using DPAPI.
4. The user highlights text and presses the hotkey.
5. The capture engine extracts selected text and surrounding context.
6. The client sends the payload to the backend over an authenticated WebSocket.
7. The backend classifies the request and streams the explanation back in real time.
8. The overlay renders the response immediately as tokens arrive.
9. After stream completion, the backend writes one final request log row to the hosted DB.
10. The user can submit a single thumbs up or thumbs down reaction for the visible response.

## Architecture

### Client
- WPF app for tray, auth flow, hotkey, and overlay
- Capture engine for selected text and background context
- Secure token storage with DPAPI
- WebSocket client with retry and reconnect behavior

### Backend
- Hono HTTP + WebSocket server
- `/auth/redeem-code`, `/auth/refresh`, `/auth/logout`
- `/ws/stream` with access-token validation on connection
- Prompt, classification, and model routing services
- Async request logging after stream completion

### Data Layer
Hosted Supabase/Postgres is used for:
- `participants`
- `redeem_codes`
- `refresh_tokens`
- `sessions`
- `request_logs`

Local client logs still exist under `runlogs/` for debugging and support.

## Repository Structure

```text
/client
  /Engine
    /Strategies
    /Classifiers
    /Detectors
    /Managers
    /Models
  App.xaml.cs
  BackendClient.cs
  AuthApiClient.cs
  AuthSessionManager.cs
  SecureTokenStore.cs
  LoginWindow.xaml
  OverlayWindow.xaml

/backend
  /db
    /migrations
    seed_redeem_codes.sql
  /scripts
    check-db-config.js
  /src
    /config
    /db
    /middleware
    /routes
    /services
  index.js
  .env
  .env.example

/dist
  /backend
  /client
  /tester-bundle

/docs and root markdown
  README.md
  deplyplan.md
  launch-docs.md
  release-checklist.md
  pilot-user-guide.md
  balancework.md
```

## Current DB Logging Shape

The hosted DB request log is intended to store the full final request record used for case-study analysis.

Current target fields include:
- `participant_id`
- `session_id`
- `request_id`
- `timestamp`
- `environment_type`
- `process_name`
- `usage_context`
- `window_title`
- `selected_text`
- `background_context`
- `selected_method`
- `background_method`
- `is_partial`
- `is_unsupported`
- `task_type`
- `response_text`
- `time_to_first_token_ms`
- `total_response_time_ms`
- `status`

## Performance Notes

The main user-visible path is intentionally preserved.

Important runtime decisions:
- DB writes happen after stream completion
- there are no per-token DB writes
- auth refresh is lightweight and separate from capture work
- capture logic was not redesigned
- forced browser OCR debug behavior was removed

This means the current deployment/auth work should not materially change the core capture speed or first-token experience.

## Local Development

### Backend

```powershell
cd backend
npm install
npm run check
npm run dev
```

### Client

```powershell
dotnet build client\CodeExplainer.csproj -nologo
dotnet run --project client\CodeExplainer.csproj
```

## Hosted Deployment State

Current hosted backend shape:
- Azure App Service hosts the backend
- Supabase hosts auth/session/request log tables
- WPF client stays local on each tester machine

Verified:
- Azure backend deploy workflow is configured to deploy only `backend/`
- hosted health endpoint responds successfully
- hosted redeem / refresh / logout behavior has been verified directly against Azure
- hosted WebSocket explanation and DB logging have been verified directly against Azure
- production tester bundle launches and points to the hosted backend

Not yet verified end to end:
- manual redeem-code login from the live packaged WPF sign-in window
- manual explain request from the live packaged WPF client using real text selection and hotkey
- packaged-client flow on a separate clean Windows machine

## Immediate Next Step

The next concrete action is:
1. complete one manual packaged-client sign-in with a fresh redeem code
2. run one manual packaged-client explanation request using the real hotkey
3. confirm the new `participants`, `refresh_tokens`, `sessions`, and `request_logs` rows from that packaged-client path
4. validate the tester bundle on a clean Windows machine

## Important Documents

- `deplyplan.md`: current deployment plan and rollout path
- `launch-docs.md`: launch and environment setup guide
- `release-checklist.md`: release and verification checklist
- `pilot-user-guide.md`: tester-facing usage guide
- `balancework.md`: detailed remaining work
