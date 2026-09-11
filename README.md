# simpleDocs

This repository contains `simpleDocs`, a Windows-first OS-level assistant that explains highlighted code, browser text, and terminal output inside a floating overlay without forcing the user to switch windows.

## Current Architecture

The main architecture is intentionally preserved.

- C# / .NET 8 / WPF desktop client
- Native Windows capture pipeline using UIA, MSAA, clipboard fallback, console APIs, and OCR
- Node.js backend with Hono
- WebSocket streaming for live responses
- Optional Supabase/Postgres support for account-based deployments and study logging
- Azure App Service for the hosted backend
- Groq as the current primary model provider, with OpenRouter available as a backend-side fallback path

## Current Product Status

The codebase builds as a pilot candidate, but the hosted system is currently a release no-go.

Live check on 2026-08-30:

- the configured Azure App Service returns `403` because the web app is stopped
- the configured Supabase hostname does not resolve
- the previously configured Groq model was retired and has been replaced in this branch with `openai/gpt-oss-120b`

Implemented now:

- native capture pipeline is in place
- overlay streaming response flow is in place
- short overlay-focused explanation style is in place
- production desktop builds now run without login or redeem codes
- the older redeem-code auth implementation remains available for future protected deployments
- Windows secure token storage with DPAPI is implemented
- protected and anonymous WebSocket modes are implemented; current production builds use anonymous mode
- hosted request logging is implemented
- thumbs up / thumbs down feedback is implemented
- thumbs feedback is stored in `request_logs.feedback_reaction`
- production client publish works
- tester zip package works
- client can start automatically with Windows using a Registry `Run` entry
- Windows auto-start is ON by default and can be toggled from the tray menu
- Groq fallback-key support is implemented in the backend

Current remaining rollout work:

- restart/redeploy the Azure App Service and confirm its production environment
- restore or replace the Supabase project and apply all migrations
- set the hosted `GROQ_MODEL` to a currently available model
- run one final clean-machine launch of the packaged client outside the dev machine
- validate the tester package on at least one additional Windows environment
- complete internal pilot monitoring and support workflow
- rotate any temporary development secrets before broader external rollout

## Runtime Flow

1. User launches the Windows client.
2. The app runs hidden to tray and registers the global hotkey without a login prompt.
5. The user highlights text and presses the hotkey.
6. The capture engine extracts selected text and surrounding context.
7. The client sends the payload to the backend over a WebSocket.
8. The backend classifies the request and streams the explanation back in real time.
9. The overlay renders the response immediately as tokens arrive.
10. Account-linked request logging and feedback are disabled in anonymous mode.

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

Verified locally on 2026-08-30:

- backend syntax and dependency audits pass
- local health and Groq WebSocket streaming pass
- client Release build and self-contained Production publish pass
- tester zip builds correctly from the current repo
- direct `CodeExplainer.exe` launch is the supported packaged path

Remaining operational validation:

- restore and revalidate Azure, Supabase, hosted auth, logging, and feedback
- one clean-machine packaged launch outside the development machine
- broader multi-app pilot validation across real tester machines

## Immediate Next Steps

1. restore the hosted backend and database
2. validate hosted health and anonymous WebSocket streaming
3. validate the latest zip on one clean Windows machine
4. configure Azure quotas and monitoring for the public endpoint
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
