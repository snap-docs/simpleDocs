# Launch Docs

## Purpose

This guide explains how to launch the current system in development, hosted validation, and pilot environments without changing the core architecture.

## Supported Runtime Shape

The current intended runtime is:

- Windows desktop client on the user machine
- hosted backend reachable over HTTPS / WSS
- hosted Supabase/Postgres for auth and request logs
- local dev mode still supported through localhost

## Current Hosted State

Verified now:

- Azure App Service backend is deployed
- hosted health endpoint responds
- hosted redeem-code login responds successfully
- hosted refresh and logout flow respond successfully
- hosted authenticated WebSocket explain flow responds successfully
- hosted request logging writes rows successfully
- feedback updates `request_logs.feedback_reaction`
- production tester bundle builds successfully

Still recommended before broader rollout:

- one clean-machine validation of the packaged client
- broader multi-environment pilot validation

## Prerequisites

### Backend host

- Azure App Service
- Node.js runtime configured by App Service
- internet access to the selected AI provider
- internet access to Supabase
- real backend environment values in Azure

### Client machine

- Windows 10 or Windows 11
- network access to the backend URL
- packaged client build or local .NET 8 desktop runtime for development

## Backend Environment Setup

Azure App Service environment variables should include at least:

```env
APP_ENV=production
AI_PROVIDER=groq
GROQ_API_KEY=your_primary_groq_key
GROQ_API_KEY_FALLBACK=your_secondary_groq_key
GROQ_MODEL=llama-3.3-70b-versatile
OPENROUTER_API_KEY=your_openrouter_key_if_used
SUPABASE_URL=https://your-project.supabase.co
SUPABASE_ANON_KEY=your_anon_key
SUPABASE_SERVICE_ROLE_KEY=your_service_role_key
ACCESS_TOKEN_SECRET=your_random_secret
SKIP_AUTH=false
PUBLIC_APP_URL=https://your-azure-app.azurewebsites.net
```

### Important notes

- `SKIP_AUTH=false` is required for hosted pilot deployment
- `SUPABASE_SERVICE_ROLE_KEY` should be present for auth and logging reliability
- `ACCESS_TOKEN_SECRET` should be your own generated backend secret
- add `GROQ_API_KEY_FALLBACK` in Azure if you want the hosted backend to use the secondary Groq key
- after changing Azure env vars, save/apply and restart the Web App

## Database Setup

Apply these migrations in order:

1. `backend/db/migrations/001_auth_and_study_schema.sql`
2. `backend/db/migrations/002_request_logs_extended_fields.sql`
3. `backend/db/migrations/003_request_feedback_and_trim_logs.sql`

The active hosted tables should then be:

- `participants`
- `redeem_codes`
- `refresh_tokens`
- `request_logs`

## Client Config Setup

The client loads environment-aware config from:

- `client/appsettings.json`
- `client/appsettings.Staging.json`
- `client/appsettings.Production.json`

Current staging and production configs point to the hosted Azure backend.

For published pilot builds, `publish-client.ps1` stamps the selected environment into the bundled `appsettings.json`, so users can launch `CodeExplainer.exe` directly.

## Local Development Launch

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

## Package Build

Build the current production package with:

```powershell
.\publish-client.ps1 -Configuration Release -Runtime win-x64 -EnvironmentName Production
.\prepare-tester-bundle.ps1 -ClientDist '.\dist\client' -OutputRoot '.\dist\tester-bundle' -EnvironmentName Production
Compress-Archive -Path '.\dist\tester-bundle\*' -DestinationPath '.\dist\simpleDocs-direct-exe-1.1.0-pilot.zip' -Force
```

Current package contents should be:

- `app\CodeExplainer.exe`
- `app\appsettings.json`
- `docs\final-tester-package-guide.md`
- `docs\chatgpt-tester-plan-prompt.md`
- `README-FIRST.txt`

## Hosted Readiness Check

Run:

```powershell
cd backend
npm run check
npm run check:db
```

Manual hosted checks:

1. open `/api/health`
2. test `/auth/redeem-code`
3. confirm the packaged app points to the hosted URLs
4. confirm one request is logged in `request_logs`

## Manual Hosted Verification

Before handing a build to testers, verify these manually:

1. backend health endpoint responds
2. redeem-code login succeeds
3. access token refresh works after restart
4. authenticated WebSocket connect succeeds
5. one explanation is streamed back
6. `participants`, `refresh_tokens`, and `request_logs` receive expected rows
7. one visible response can store `feedback_reaction`
8. logout revokes the refresh token cleanly

## Support Inputs To Collect

If a pilot tester reports an issue, collect:

- tester redeem code or tester id
- time of the issue
- application used when capture failed
- whether sign-in worked
- whether the overlay appeared
- whether feedback was accepted
- screenshots or local logs if available

## Known Remaining Launch Gaps

- one clean-machine package validation is still recommended before broader rollout
- capture quality still needs broader validation across more real tester environments
