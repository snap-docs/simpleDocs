# simpleDocs

This repository contains `simpleDocs`, a Windows-first OS-level assistant that explains highlighted code, browser text, and terminal output inside a floating overlay without forcing the user to switch windows.

## Current Architecture

The main architecture is intentionally preserved.

- C# / .NET 8 / WPF desktop client
- Native Windows capture pipeline using UIA, MSAA, clipboard fallback, console APIs, and OCR
- Node.js backend with Hono
- WebSocket streaming for live responses
- Hosted Supabase/Postgres for auth and study logging
- Azure App Service for the hosted backend
- Groq as the current primary model provider, with OpenRouter available as a backend-side fallback path

## Current Product Status

The project is now in pilot-ready implementation state with a few remaining rollout checks.

Implemented now:

- native capture pipeline is in place
- overlay streaming response flow is in place
- short overlay-focused explanation style is in place
- one-time redeem-code auth is implemented
- Windows secure token storage with DPAPI is implemented
- authenticated WebSocket explain flow is implemented
- hosted request logging is implemented
- thumbs up / thumbs down feedback is implemented
- thumbs feedback is stored in `request_logs.feedback_reaction`
- production client publish works
- tester zip package works
- client can start automatically with Windows using a Registry `Run` entry
- Windows auto-start is ON by default and can be toggled from the tray menu
- Azure App Service backend is live
- hosted `/api/health` is live and returning `ok`
- hosted redeem-code login, refresh, and logout flows are working
- hosted DB connectivity checks are working
- Groq fallback-key support is implemented in the backend

Current remaining rollout work:

- run one final clean-machine launch of the packaged client outside the dev machine
- validate the tester package on at least one additional Windows environment
- complete internal pilot monitoring and support workflow
- rotate any temporary development secrets before broader external rollout

## Runtime Flow

1. User launches the Windows client.
2. The client restores the stored session or prompts for a redeem code.
3. The client stores tokens securely on Windows using DPAPI.
4. The app runs hidden to tray and registers the global hotkey.
5. The user highlights text and presses the hotkey.
6. The capture engine extracts selected text and surrounding context.
7. The client sends the payload to the backend over an authenticated WebSocket.
8. The backend classifies the request and streams the explanation back in real time.
9. The overlay renders the response immediately as tokens arrive.
10. After stream completion, the backend writes one final `request_logs` row to the hosted DB.
11. The user can submit a single thumbs up or thumbs down reaction for the visible response.

## Current Data Model

Hosted Supabase/Postgres is used for:

- `participants`
- `redeem_codes`
- `refresh_tokens`
- `request_logs`

`request_logs` is the main study-analysis table and currently stores:

- `participant_id`
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
- `task_type`
- `response_text`
- `time_to_first_token_ms`
- `total_response_time_ms`
- `status`
- `feedback_reaction`

The system no longer stores `session_id`, `is_partial`, `is_unsupported`, or `feedback_at` in the hosted request log table.

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
  WindowsStartupManager.cs

/backend
  /db
    /migrations
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
  simpleDocs-direct-exe-1.1.0-pilot.zip
```

## Packaging

The current tester package is a portable zip, not a traditional installer.

Current packaged contents:

- `app\CodeExplainer.exe`
- `app\appsettings.json`
- `docs\final-tester-package-guide.md`
- `docs\chatgpt-tester-plan-prompt.md`
- `README-FIRST.txt`

The main tester entry point is `app\CodeExplainer.exe`.

## Local Development

### Backend

```powershell
cd backend
npm install
npm run check
npm run check:db
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
- Supabase hosts auth and request-log tables
- WPF client stays local on each tester machine
- production client config points to the hosted Azure backend
- Windows client remains responsible for capture, hotkey, overlay, and local auth state

Verified now:

- hosted health endpoint responds successfully
- hosted auth flow works
- hosted request logging works
- feedback updates `request_logs.feedback_reaction`
- tester zip builds correctly from the current repo
- direct `CodeExplainer.exe` launch is the supported packaged path

Remaining operational validation:

- one clean-machine packaged launch outside the development machine
- broader multi-app pilot validation across real tester machines

## Immediate Next Steps

1. validate the latest zip on one clean Windows machine
2. issue redeem codes to internal pilot users
3. monitor `request_logs` and `feedback_reaction` during pilot use
4. collect capture-quality feedback from editors, browsers, and terminals
5. rotate temporary development secrets before a wider external rollout

## Important Documents

- `deplyplan.md`: current deployment topology and rollout plan
- `launch-docs.md`: local, hosted, and package launch guide
- `release-checklist.md`: release and verification checklist
- `final-tester-package-guide.md`: tester-facing package guide
- `pilot-user-guide.md`: short tester usage guide
- `balancework.md`: remaining rollout work after current implementation
- `CAPTURE_PIPELINE.md`: current capture architecture reference
- `classifier-explained.md`: how `is_partial` and `task_type` are produced
