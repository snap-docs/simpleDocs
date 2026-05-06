# Pilot Support Runbook

Use this runbook during the internal pilot whenever a tester reports a sign-in, hotkey, overlay, capture, feedback, or startup problem.

## Intake Fields

Collect these fields before debugging:

- tester name or tester code
- sign-in method used: Google, email/password, or legacy redeem code
- approximate local time of the issue
- Windows version if known
- app version or package zip name
- app being used when the issue happened
- selected text or a short description of the selected content
- what the tester expected
- what actually happened
- screenshot or screen recording if available

Do not ask testers to send access tokens, refresh tokens, API keys, database credentials, or raw secrets.

## First Response

1. Confirm whether the tester extracted the zip before running the app.
2. Confirm they launched `app\CodeExplainer.exe`.
3. Ask whether the tray icon is visible.
4. Ask whether sign-in completed.
5. Ask whether the hotkey produced any overlay, popup, or no visible response.

## Fast Classification

Classify the issue before changing code:

- `package`: app missing files, zip not extracted, wrong package version, Windows blocked launch
- `auth`: sign-in failed, used Google, email/password, or a legacy redeem code, session restore failed, logout/refresh issue
- `startup`: tray missing, auto-start failed, app exits before ready
- `hotkey`: no hotkey registration, conflicting shortcut, no response after hotkey
- `capture`: overlay appears but selected/background text is wrong or missing
- `stream`: overlay opens but response does not stream or connection popup appears
- `feedback`: thumbs up/down does not save or creates an error
- `data`: request log row missing or feedback row not updated

## Evidence To Check

Start with local runtime logs:

- `runlogs/client_live.log`
- `runlogs/backend_live.log`
- `runlogs/backend_live.err.log`

Use Azure and hosted data checks when the local log points to hosted behavior:

- Azure App Service health endpoint
- Azure Log stream
- Supabase `participants`
- Supabase `auth_provider_links`
- Supabase `auth_sessions`
- Supabase `refresh_tokens`
- Supabase `request_logs`

## Support Bundle

From the repo root, run:

```powershell
.\export-support-bundle.ps1
```

The script creates a zip under `dist\support\` with logs, config snapshots, release docs, and this runbook.

Before sharing a support bundle outside the core team, review it for accidental secrets or private tester content.

## Resolution Notes

For every issue, record:

- issue class
- root cause if known
- whether it was user setup, package, backend, auth, capture, model/provider, or DB
- exact fix or workaround
- whether a new package is required
- whether the tester needs a fresh legacy redeem code, a Google account action, or another account action

## Escalation Rules

Escalate immediately if:

- multiple testers cannot sign in
- hosted health fails
- request logging stops for working explanations
- feedback writes fail for multiple users
- the packaged app crashes before tray startup
- a tester reports private or wrong-window content being captured

## Closeout Criteria

Close a pilot support issue only when:

- the tester confirms the workflow works again, or the limitation is documented
- the latest log or DB row supports the fix
- any needed code/doc change is committed on the feature branch
- any package refresh has been rebuilt and named clearly
