# Implementation Status Plan

This document tracks the current implementation status of the deployment-ready system.

## Current Implemented State

### Core assistant flow

- native Windows capture pipeline remains intact
- overlay rendering remains intact
- WebSocket streaming remains intact
- explanation style is tuned for the small overlay
- thumbs up / thumbs down feedback exists in the overlay

### Auth and session

- redeem-code login UI exists in the WPF client
- backend redeem / refresh / logout endpoints exist
- client stores tokens securely on Windows
- session restore and silent refresh are implemented
- WebSocket auth is validated on connect

### Request logging

- request logging happens after stream completion
- request logging writes a single final row per request
- hosted DB connectivity checks are available
- DB schema and seed SQL files exist in the repo
- feedback is stored as `feedback_reaction` in `request_logs`
- `session_id`, `is_partial`, and `is_unsupported` are no longer part of the active hosted request-log design

### Packaging and startup

- client publish script exists
- tester bundle script exists
- final direct-exe package exists
- packaged app launches directly from `CodeExplainer.exe`
- Windows auto-start support exists through a Registry `Run` entry
- startup can be toggled from the tray menu

### Hosted validation

- Azure backend is deployed
- hosted health endpoint works
- hosted redeem-code login works
- hosted refresh/logout flow works
- hosted authenticated WebSocket explanation works
- hosted DB logging works
- hosted feedback logging works
- client production/staging config points to the hosted backend

## Remaining Work

### High priority

1. run the latest package on a clean Windows machine
2. run a short internal pilot with real users
3. confirm the support and issue-triage process
4. rotate temporary development secrets before broader rollout

### Medium priority

1. validate on multiple Windows environments
2. verify more editor/browser/terminal combinations
3. review provider throttling behavior during pilot usage
4. monitor request logs and feedback reactions regularly

### Lower priority after pilot starts

1. refine onboarding copy based on tester confusion
2. tighten metrics review process
3. decide long-term release/update workflow

## Current Truth

The main implementation work is complete.

The remaining work is mostly rollout validation, package validation on more machines, and pilot operations.

The main product architecture should not be redesigned at this stage.
