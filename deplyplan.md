# Deployment README

## Purpose

This file is the current deployment reference for `simpleDocs`.

It only covers deployment, hosting, tester delivery, runtime behavior, and operational checks.

It does not document full product architecture or capture internals.

## Current Deployment State

The project is in hosted pilot stage.

Current live topology:

- Windows WPF client runs on the tester machine
- Node.js Hono backend runs on Azure App Service
- Supabase/Postgres stores auth and study logs
- WebSocket streaming is used for explanations
- redeem-code auth is required in hosted production mode

## Hosted Services

### Azure backend

- App Service name: `simpleDocs`
- Hosted backend base URL: `https://simpledocs-e3ahgecnbaf2dcfu.centralindia-01.azurewebsites.net`
- Health endpoint: `https://simpledocs-e3ahgecnbaf2dcfu.centralindia-01.azurewebsites.net/api/health`
- WebSocket endpoint: `wss://simpledocs-e3ahgecnbaf2dcfu.centralindia-01.azurewebsites.net/ws/stream`

### Database

Hosted Postgres is provided through Supabase.

Active deployment tables:

- `participants`
- `redeem_codes`
- `refresh_tokens`
- `request_logs`

## Client Runtime Modes

The client can run in two main modes.

### Development mode

Development mode points to localhost.

Expected values:

- API: `http://localhost:3000`
- WebSocket: `ws://localhost:3000`
- auth can be disabled locally

This mode is only for local development and debugging.

### Production mode

Production mode points to the Azure backend.

Expected values:

- API: `https://simpledocs-e3ahgecnbaf2dcfu.centralindia-01.azurewebsites.net`
- WebSocket: `wss://simpledocs-e3ahgecnbaf2dcfu.centralindia-01.azurewebsites.net`
- auth is enabled

Important:

If the app starts in `Development`, it will still try `localhost` and the hosted deployment will not be tested correctly.

## Auth Behavior In Production

Production requires redeem-code login.

Current auth flow:

1. User opens the client
2. Login window asks for redeem code
3. Client calls `POST /auth/redeem-code`
4. Backend returns `access_token` and `refresh_token`
5. Client stores them locally with Windows DPAPI
6. Client uses the access token for backend calls
7. Client refreshes the session silently when needed

The user should stay signed in for around 30 days unless the session is revoked or unrecoverable.

## WebSocket Auth Behavior

Hosted `/ws/stream` is protected.

The client now sends the access token in both supported ways:

- `Authorization: Bearer <token>` request header
- `?access_token=<token>` query string

This is intentional because hosted WebSocket auth can be more reliable when the token is also available in the connection URL.

## Tester Delivery Model

The tester receives a portable Windows package.

Current delivery model:

- unzip package
- open the extracted app folder
- run `CodeExplainer.exe`
- sign in once with redeem code
- use the app normally after that

The tester does not need to run PowerShell or command-line startup commands for normal usage.

## Auto-Start Behavior

The client supports Windows auto-start through the Registry `Run` entry.

Current behavior:

- enabled by default
- can be toggled from the tray menu
- app starts hidden and waits for use

## Production Config Files

Production client config is currently defined in:

- [appsettings.Production.json](</d:/PROJECTS/Startup/prototype4 - Copy (2)/client/appsettings.Production.json>)
- [appsettings.Staging.json](</d:/PROJECTS/Startup/prototype4 - Copy (2)/client/appsettings.Staging.json>)

The default development config is:

- [appsettings.json](</d:/PROJECTS/Startup/prototype4 - Copy (2)/client/appsettings.json>)

If the running app is using `localhost`, it is not in production mode.

## Azure Checks

### What Azure Overview tells us

Azure Overview confirms:

- app exists
- app service is running
- default domain
- runtime stack
- health check summary

This is useful but not enough to prove the full app flow works.

### What Azure Activity Log tells us

Azure Activity Log only shows Azure management events.

Examples:

- restart
- config change
- deployment
- delete

It does not show application-level request failures or WebSocket handshake failures.

### What to use for runtime problems

For runtime troubleshooting, use:

- Azure `Overview`
- Azure `Log stream`
- `https://.../api/health`
- local client log

## Local Runtime Logs

Main runtime log files:

- [client_live.log](</d:/PROJECTS/Startup/prototype4 - Copy (2)/runlogs/client_live.log>)
- [backend_live.log](</d:/PROJECTS/Startup/prototype4 - Copy (2)/runlogs/backend_live.log>)
- [backend_live.err.log](</d:/PROJECTS/Startup/prototype4 - Copy (2)/runlogs/backend_live.err.log>)

These are the first files to inspect during deployment testing.

## Current Known Deployment Lessons

These are the important operational lessons learned during hosted testing.

### 1. Azure can be healthy while the client still fails

If the client shows a connection popup, that does not automatically mean Azure is down.

The backend can still be healthy while:

- the client is in the wrong environment mode
- the client has no valid token
- the redeem code has already been used
- the WebSocket auth handshake is rejected

### 2. Hotkey will not register if startup auth is not completed

If production login fails, startup aborts before hotkey registration.

That means:

- no tray-ready state
- no `Ctrl+Shift+Space`
- no normal explain flow

### 3. Used redeem codes cannot be reused for first-time login

If a code is already marked used, startup login will fail.

In that case the tester needs:

- a fresh unused redeem code
- or a still-valid stored session from a previous successful sign-in

## Current Pilot-Ready Checklist

The deployment is considered pilot-ready when all of these are true:

- Azure health endpoint returns `200`
- production client launches with hosted URL, not localhost
- redeem-code login succeeds
- hotkey registers after login
- hosted WebSocket explanation succeeds
- request log row is written to Supabase
- feedback reaction works
- portable tester package works on a clean Windows machine

## Manual Production Verification

Recommended hosted verification sequence:

1. Launch the client in production mode
2. Confirm client log shows hosted Azure URL
3. Enter a fresh redeem code
4. Confirm hotkey registration appears in log
5. Select text and trigger explanation
6. Confirm WebSocket connect line uses `wss://...azurewebsites.net/ws/stream`
7. Confirm no connection error popup appears
8. Confirm request row is stored in Supabase

## Current Remaining Deployment Risks

The remaining deployment risks are operational, not architectural.

Main ones:

- tester may accidentally run a development-mode build
- tester may try an already-used redeem code
- client packaging still needs repeated clean-machine validation
- production testing should be repeated after every package refresh

## Recommended Immediate Rule

For hosted testing and tester delivery, always use:

- production config
- hosted Azure backend
- fresh redeem code
- runtime log verification after first launch

That is the safest deployment path for the current system.
