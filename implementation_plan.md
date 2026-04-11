# Implementation Status Plan

This document tracks the current implementation status of the deployment-ready system.

## Completed Work

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
- session id and request id flow exist
- request logging happens after stream completion
- hosted DB connectivity checks are available
- DB schema and seed SQL files exist in the repo
- request log field mapping has been extended for study fields

### Deployment tooling
- client publish script exists
- backend package script exists
- tester bundle script exists
- support bundle export script exists
- Azure GitHub Actions workflow deploys only `backend/`
- release and launch docs exist

### Hosted validation
- Azure backend is deployed
- hosted health endpoint works
- client production/staging config points to the hosted backend
- production bundle launches correctly
- sign-in UI has been polished

## Remaining Work

### High priority
1. fix Azure `ACCESS_TOKEN_SECRET` handling
2. run one full redeem-code login test against hosted backend
3. run one full explain request against hosted backend
4. verify DB rows are created correctly
5. validate tester bundle on a clean Windows machine

### Medium priority
1. validate on multiple Windows environments
2. verify logout and expired-session recovery with real backend
3. prepare internal pilot support workflow
4. rotate development secrets before external users

### Lower priority after pilot starts
1. refine onboarding copy based on tester confusion
2. tighten metrics review process
3. decide long-term release/update workflow

## Current Truth

The remaining work is now mostly hosted validation and pilot hardening.

The main product architecture should not be redesigned at this stage.
