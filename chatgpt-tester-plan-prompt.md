# ChatGPT Prompt For Install And Usage Plan

Use the prompt below in ChatGPT if you want a clean tester-facing installation guide, onboarding plan, and usage checklist for this package.

```text
I have a Windows desktop app called simpleDocs that is distributed as a zip file, not a traditional installer.

The tester flow is:
- user downloads the zip
- user extracts the zip to a normal folder
- inside the extracted folder there is app\\CodeExplainer.exe
- user launches CodeExplainer.exe directly
- on first launch the app asks for a one-time redeem code
- after successful sign-in, the app stays signed in on that Windows user profile
- on later launches the user usually does not need to enter the code again
- user selects code, terminal output, or technical text
- user presses the app hotkey
- the app shows a floating explanation overlay

Important details:
- this is not a traditional installer
- the user should not run the app from inside the zip viewer
- the user should extract the zip first
- the user should not need command line steps for normal use
- if we ship a new version later, the user can extract the new zip and launch the new exe
- in most cases the user should remain signed in on the same Windows account

Create a complete but simple package for external testers with:
1. a short installation guide
2. a first-run guide
3. a daily usage guide
4. an update guide for newer versions
5. a troubleshooting section
6. a short support message template testers can send back

Keep it practical, clean, and non-technical.
Write it so real external pilot users can follow it easily.
```
