# Deployment And Packaging

The deployment shape is split between a local Windows desktop client and a hosted backend.

The backend can run locally during development or in Azure App Service for hosted pilot use. The client runs on the tester's Windows machine and points at whichever backend URL is stamped into its config.

## Local Development

The local backend command is:

```powershell
cd backend
npm install
npm run check
npm run dev
```

The local client command is:

```powershell
dotnet build client\CodeExplainer.csproj -nologo
dotnet run --project client\CodeExplainer.csproj
```

The root `run.ps1` script starts both. It kills anything already listening on port `3000`, stops stale `CodeExplainer` processes, starts the backend, and then runs the client.

## Client Build Output

The project uses `Directory.Build.props` to place client build output under the root `build` directory. That avoids mixing normal `bin` and `obj` output into source folders and makes packaging cleaner.

The client project targets:

```text
net8.0-windows10.0.19041.0
```

That target is Windows-specific because the app uses WPF, Windows Forms tray APIs, registry APIs, and native Windows capture behavior.

## Environment Config

The client reads:

- `client/appsettings.json`
- `client/appsettings.Staging.json`
- `client/appsettings.Production.json`

The publish scripts can stamp the selected environment into the packaged `appsettings.json`. That lets a tester run `CodeExplainer.exe` directly without needing to understand environment variables.

Local development can use:

```json
{
  "Auth": {
    "Enabled": false
  }
}
```

Hosted pilot builds should keep auth enabled and point to HTTPS/WSS backend URLs.

## Backend Config

The backend reads `.env` files through `loadEnv.js`.

Important backend values:

- `PORT`
- `APP_ENV`
- `AI_PROVIDER`
- `GROQ_API_KEY`
- `GROQ_API_KEY_FALLBACK`
- `OPENROUTER_API_KEY`
- `GEMINI_API_KEY`
- `SUPABASE_URL`
- `SUPABASE_SERVICE_ROLE_KEY`
- `ACCESS_TOKEN_SECRET`
- `SKIP_AUTH`
- `REQUEST_LOGS_TABLE`

`backend/.env` is ignored by git. Secrets should live there locally and in the host environment for deployment.

## Backend Checks

`npm run check` runs Node syntax checks over the backend entry point, routes, middleware, provider clients, and database helpers.

This is not a full test suite, but it catches syntax errors before deploy or launch.

`npm run check:db` validates database configuration through `scripts/check-db-config.js`.

## Package Build

The production packaging flow is:

```powershell
.\publish-client.ps1 -Configuration Release -Runtime win-x64 -EnvironmentName Production
.\prepare-tester-bundle.ps1 -ClientDist '.\dist\client' -OutputRoot '.\dist\tester-bundle' -EnvironmentName Production
Compress-Archive -Path '.\dist\tester-bundle\*' -DestinationPath '.\dist\simpleDocs-direct-exe-1.1.0-pilot.zip' -Force
```

The tester package contains:

- `app\CodeExplainer.exe`
- `app\appsettings.json`
- tester docs
- `README-FIRST.txt`

The supported tester entry point is the direct executable.

## Hosted Backend

The documented hosted target is Azure App Service.

Hosted backend requirements:

- Node.js runtime
- internet access to the AI provider
- internet access to Supabase
- real environment variables
- `SKIP_AUTH=false`
- service role key for database writes
- production `PUBLIC_APP_URL`

The hosted health endpoint is `/api/health`.

## Supabase Migrations

Database setup applies migrations in order:

1. `backend/db/migrations/001_auth_and_study_schema.sql`
2. `backend/db/migrations/002_request_logs_extended_fields.sql`
3. `backend/db/migrations/003_request_feedback_and_trim_logs.sql`

The active tables are:

- `participants`
- `redeem_codes`
- `refresh_tokens`
- `request_logs`

## Release Verification

Before a pilot build goes out, verify:

- backend health responds
- redeem-code login works
- token refresh works after restart
- authenticated WebSocket streaming works
- one request appears in `request_logs`
- feedback updates `feedback_reaction`
- logout revokes refresh token
- packaged client starts on a clean Windows machine

The clean-machine check matters because a dev machine can hide missing runtime assumptions.

## Secret Hygiene

Do not commit real `.env` values. The repo intentionally ignores `backend/.env`.

The public client config can include backend URLs, but it should not include AI provider keys, Supabase service role keys, access token secrets, or refresh tokens.

If a key is shown in screenshots, terminal output, or chat, rotate it before broader deployment. Local testing can continue briefly, but production secrets should be treated as replaceable.
