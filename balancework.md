# Balance Work

This document tracks the real remaining work after the current implementation and hosted deployment.

## Current Position

### Implemented and verified in the repo

- core WPF + native capture + Hono + WebSocket architecture is preserved
- overlay explanation flow is working
- compact explanation style is working
- redeem-code auth is working in backend and client
- secure token storage is working
- authenticated WebSocket flow is working
- hosted Supabase connectivity is working
- hosted request logging is working
- thumbs feedback is working
- feedback is stored in `request_logs.feedback_reaction`
- Azure App Service backend is deployed and healthy
- production package build works
- Windows auto-start support is implemented
- auto-start can be toggled from the tray menu
- Groq fallback-key support is implemented in the backend

### What is no longer a repo-code blocker

- auth/session implementation
- hosted backend connection
- request logging implementation
- feedback implementation
- package generation
- direct `CodeExplainer.exe` launch flow

## Real Remaining Work

The remaining work is now mostly operational and validation work, not major implementation work.

## Priority A: Final rollout validation

### A1. Clean-machine package validation

Status: still needed

Work:

- unzip the latest package on a machine that is not the development machine
- launch `app\CodeExplainer.exe`
- confirm sign-in, tray behavior, hotkey flow, and overlay behavior
- confirm the app can restart and restore the stored session

Why it matters:

- this is the last practical package check before wider user distribution

### A2. Internal pilot validation

Status: still needed

Work:

- test with a small group of real users
- cover VS Code, browser, Windows Terminal, and at least one more environment
- collect explanation-quality and capture-quality feedback
- review logs and thumbs feedback

Why it matters:

- this produces real usage evidence before expanding to more testers

### A3. Operations and support readiness

Status: still needed

Work:

- define where testers report issues
- define who reviews logs and DB rows
- define how redeem codes are tracked per tester
- prepare a short privacy/support message for pilot users

Why it matters:

- pilot users will need a reliable response path when something goes wrong

## Priority B: Hardening and rollout safety

### B1. Multi-environment QA

Status: partly done

Work:

- Windows 10 and Windows 11 validation
- multiple DPI/scaling tests
- multiple monitor checks
- more app coverage beyond the main dev machine

### B2. Secret hygiene

Status: still needed

Work:

- rotate any temporary development API keys
- confirm only final deployment secrets remain in Azure and local env files
- confirm no secrets are bundled into the published client files

### B3. Hosted monitoring rhythm

Status: still needed

Work:

- decide how often to review `request_logs`
- review `feedback_reaction` during pilot
- watch provider failures or throttling behavior
- confirm fallback-key behavior if the main Groq key hits limits

## Current Validation Matrix

### Auth

- redeem code accepted
- invalid code rejected cleanly
- used code rejected cleanly
- refresh works
- logout works
- app restart restores session

### Streaming

- authenticated WebSocket connects
- first token arrives promptly
- complete message is received
- overlay status updates correctly

### Logging

- request id is unique
- final request row is written only once
- DB failure does not block first visible output
- `feedback_reaction` can be updated later from the overlay

### Package

- zip is generated successfully
- package contains only the needed runtime and tester docs
- app launches directly from `CodeExplainer.exe`
- clean-machine validation is still recommended before wider rollout

## Suggested Execution Order

1. validate the latest package on a clean Windows machine
2. run a short internal pilot
3. monitor hosted logs and feedback reactions
4. fix pilot issues with the highest signal
5. expand to more testers

## What Should Not Change Right Now

To keep risk low before the pilot, we should not redesign these:

- capture architecture
- overlay architecture
- WebSocket streaming architecture
- WPF client platform choice
- backend framework choice

## Pilot Start Definition

We can call the system pilot-ready when:

- hosted auth works
- hosted logging works
- packaged client works
- one clean-machine package run is confirmed
- internal users complete real tasks successfully
- no critical blocker remains in capture, overlay, or session flow
