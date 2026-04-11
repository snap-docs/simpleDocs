# Final Tester Package Guide

## What this package is

This zip is a portable Windows app package for `simpleDocs`.

It is not a traditional installer.

The tester does not need to install it into `Program Files`.

They only need to unzip it and run the app.

## What the tester will see after unzipping

After extracting the zip, the folder contains:

- `app\CodeExplainer.exe`
- `app\Start-CodeExplainer.bat`
- `docs\...`

The main file the tester should use is:

`app\CodeExplainer.exe`

The `.bat` file is only an optional launcher.

## First-time setup for a tester

1. Extract the zip to a normal folder such as `Desktop\simpleDocs` or `Documents\simpleDocs`.
2. Open the extracted folder.
3. Open the `app` folder.
4. Double-click `CodeExplainer.exe`.
5. If Windows shows a security prompt, choose the option to continue if the tester trusts the app source.
6. Enter the redeem code when the sign-in window appears.
7. Wait until the tray icon shows the app is ready.

## Daily use after first sign-in

The tester does not need to enter the redeem code every time.

The app stores the signed-in session on that Windows user profile.

After the first successful sign-in, the tester can simply:

1. Open the same extracted folder again.
2. Double-click `app\CodeExplainer.exe`.
3. Select text in an editor, browser, or terminal.
4. Press the app hotkey.
5. Read the overlay response.

## Does the tester need to install it again every day

No.

They only need to unzip the package once.

After that, they can keep using the same extracted folder and launch `CodeExplainer.exe` whenever they want.

## What happens when we send a newer version later

If we send a new zip later, the tester should:

1. Close the current app if it is running.
2. Extract the new zip to a fresh folder, or replace the old extracted folder.
3. Launch the new `CodeExplainer.exe`.

Normally they should still stay signed in as the same user on the same Windows account, because the auth state is stored separately on the machine.

They only need a new redeem code if:

- the local signed-in state was deleted
- they use a different Windows user account
- they move to a different machine
- their session has been revoked or expired beyond recovery

## Best folder recommendation for testers

Tell testers not to run the app from inside the zip viewer.

They should extract it first.

Good locations:

- Desktop
- Documents
- a dedicated `simpleDocs` folder

Avoid temporary download folders if possible.

## How the tester should open the app

Use:

`app\CodeExplainer.exe`

Do not ask the tester to run terminal commands.

Do not ask the tester to use PowerShell for normal usage.

## What the tester should do inside the app

1. Highlight code, terminal output, or technical text.
2. Press the configured hotkey.
3. Wait for the floating overlay.
4. Read the explanation.
5. Optionally click thumbs up or thumbs down.

## If the tester has trouble

Ask them to report:

- the redeem code or tester id
- the time of the issue
- the app they were using
- what they selected
- what happened instead

If needed, ask them to run:

`export-support-bundle.ps1`

## Recommended message you can send to testers

Extract the zip first, then open `app\CodeExplainer.exe`. Sign in once with your redeem code. After that, you can keep using the same app folder and open the exe whenever you want. You do not need to reinstall it every time.
