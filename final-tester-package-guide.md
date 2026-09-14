# Final Tester Package Guide

## What this package is

This ZIP is a per-user Windows installation package for `simpleDocs`.

It installs into `%LOCALAPPDATA%\Programs\simpleDocs`, so administrator access is not required. The tester does not need to install the .NET runtime separately.

## What the tester will see after unzipping

After extracting the zip, the folder contains:

- `Install-simpleDocs.cmd`
- `Install-simpleDocs.ps1`
- `app\CodeExplainer.exe`
- `app\appsettings.json`
- `docs\final-tester-package-guide.md`
- `docs\chatgpt-tester-plan-prompt.md`
- `README-FIRST.txt`

The main file the tester should use is:

`Install-simpleDocs.cmd`

## First-time setup for a tester

1. Extract the zip to a normal folder such as `Desktop\simpleDocs` or `Documents\simpleDocs`.
2. Open the extracted folder.
3. Double-click `Install-simpleDocs.cmd`.
4. Wait for the successful installation message.
5. If Windows shows a security prompt, choose the option to continue if the tester trusts the app source.
6. No account, login, or redeem code is required.
7. Wait until the tray icon shows the app is ready.

## Daily use

The tester can simply:

1. Select text in an editor, browser, or terminal.
2. Press the app hotkey.
3. Read the overlay response.

By default, `simpleDocs` also starts automatically when the user signs in to Windows.

If they want to turn that off later, they can use the tray icon menu.

## Does the tester need to install it again every day

No.

They only need to extract the package and run the installer once. simpleDocs then starts automatically after Windows sign-in and is available from the Start Menu.

## What happens when we send a newer version later

If we send a new zip later, the tester should:

1. Extract the new ZIP.
2. Run `Install-simpleDocs.cmd` again.
3. The installer stops only the older installed simpleDocs process, replaces it, repairs startup integration, and launches the new version.

No account state needs to be migrated between versions or machines.

## Best folder recommendation for testers

Tell testers not to run the installer from inside the ZIP viewer.

They should extract it first.

Good locations:

- Desktop
- Documents
- a dedicated `simpleDocs` folder

Avoid temporary download folders if possible.

## How the tester should open the app

Use:

`Install-simpleDocs.cmd`

Do not ask the tester to run terminal commands.

Do not ask the tester to use PowerShell for normal usage.

## What the tester should do inside the app

1. Highlight code, terminal output, or technical text.
2. Press the configured hotkey.
3. Wait for the floating overlay.
4. Read the explanation.
5. Account-linked thumbs feedback is not shown in the current no-login build.

## If the tester has trouble

Ask them to report:

- the app version
- the time of the issue
- the app they were using
- what they selected
- what happened instead
- a screenshot if possible

## Recommended message you can send to testers

Extract the ZIP first, then run `Install-simpleDocs.cmd`. No administrator access, login, or redeem code is required. simpleDocs installs under your Windows profile and starts automatically after Windows sign-in unless you turn that off from the tray menu.
