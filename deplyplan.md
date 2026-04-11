# Deployment Plan

## Current Stage

The project is now in hosted pilot-preparation stage.

The main application flow is already preserved and working in its intended structure:
- Windows WPF client
- native capture engine
- Node.js Hono backend
- WebSocket streaming
- overlay response UI

The deployment path now includes:
- Azure App Service for the backend
- Supabase/Postgres for auth/session/request logging
- packaged Windows client for testers

## What Is Already Implemented

### Core product
- hotkey-triggered native capture flow
- overlay streaming response flow
- lightweight thumbs up / thumbs down response feedback
- compact overlay-oriented explanation style

### Auth and session
- redeem-code login
- refresh-token flow
- logout flow
- Windows secure token storage
- WebSocket auth at handshake
- protected backend routes

### Hosted data path
- Supabase/Postgres connectivity
- auth-related tables
- request log table path
- session/request id flow
- async post-response request logging
- redeem codes seeded in hosted DB

### Packaging and support
- client publish script
- backend package script
- tester bundle script
- support bundle export script
- release checklist and pilot guide

### Hosting
- Azure App Service backend deployment workflow exists
- Azure backend is live
- `/api/health` works from the hosted URL
- hosted redeem / refresh / logout flow works
- hosted authenticated WebSocket explanation works
- hosted request logging works
- client staging/production config points to the Azure backend
- production tester bundle launches successfully

## What Still Needs To Be Done

### Must do before external users
1. Run one real redeem-code login from the packaged WPF client.
2. Run one full explain request from the packaged WPF client.
3. Confirm DB rows are written to `participants`, `refresh_tokens`, `sessions`, and `request_logs` from that packaged-client path.
4. Verify the packaged tester bundle on a clean Windows machine.

### Should do before broader rollout
1. Validate on multiple Windows versions and DPI settings.
2. Validate on VS Code, browser, Windows Terminal, and classic terminal.
3. Confirm support process for logs and tester issues.
4. Freeze one build for the pilot.
5. Rotate any development API keys before external users.

## Deployment Phases

### Phase 1: Infrastructure completion
- hosted Supabase configured
- schema applied
- service-role and access-token secrets configured
- redeem codes seeded
- Azure backend deployed

### Phase 2: Hosted validation
- client points to real hosted API / WebSocket URLs
- hosted health endpoint works
- one redeem-code login works
- one request writes to `participants`, `refresh_tokens`, `sessions`, and `request_logs`
- WebSocket auth works with the live backend

### Phase 3: Internal pilot
- 3 to 5 trusted users
- packaged build only
- support bundle collection enabled
- issue triage after every tester session

### Phase 4: External pilot
- 10 to 30 users
- fixed build and fixed model configuration
- redeem-code issuance tracked manually
- thumbs feedback and DB logs reviewed regularly

## Current Risks

### Technical risks
- full live auth-to-log path still needs one confirmed end-to-end run from the packaged client
- packaged build has not yet been tested on a separate clean Windows machine

### Operational risks
- API keys in local env should be rotated before real-user rollout
- support / tester onboarding must be consistent
- privacy language must be shown clearly to testers

## Recommended Immediate Order

1. Test one real packaged-client sign-in.
2. Test one real packaged-client explanation.
3. Confirm DB inserts from that packaged-client flow.
4. Validate the bundle on a clean Windows machine.
5. Start internal pilot.

## Definition Of Pilot Ready

The system is pilot ready when all of these are true:
- client packaged build launches cleanly
- redeem-code login works against the hosted backend
- session restores after restart
- authenticated WebSocket explain request works
- final request log row is written to hosted DB
- one real packaged-client auth and hotkey flow has been verified
- thumbs feedback works in overlay
- no obvious capture or overlay regressions are present
