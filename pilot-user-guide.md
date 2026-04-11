# Pilot User Guide

## Sign In

1. Launch `Start-CodeExplainer.bat`.
2. Enter the redeem code you received.
3. Wait for the tray icon to show that the app is ready.

## Use The App

1. Highlight code, terminal output, or technical text.
2. Press the configured hotkey.
3. Read the explanation in the overlay.
4. If the response is visible, use thumbs up or thumbs down once if you want to send feedback.

## What The App Sends

The app may use:
- selected text
- surrounding background context
- window/application metadata needed for explanation quality

For pilot testing, use the tool only on content that is allowed for the study.

## If Something Goes Wrong

1. Check that your internet connection is working.
2. Close and reopen the app.
3. If sign-in fails, verify the redeem code with support.
4. If sign-in still fails after the code is confirmed, tell support that the hosted sign-in flow failed.
5. If the app still fails, export a support bundle and send it to the team.

## Export Support Bundle

Run:
`.\export-support-bundle.ps1`

This collects local logs and selected config files into a zip for support review.

## What To Send Support

- your tester code or tester id
- when the problem happened
- what app you were using
- what text you selected
- what happened instead of the expected result
- the support bundle zip
