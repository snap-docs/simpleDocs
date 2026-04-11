# Balance Work

This document tracks the real remaining work after backend hosting, client packaging, and hosted health verification.

## 1. Current Position

### Already done
- core WPF + native capture + Hono + WebSocket architecture is preserved
- overlay explanation flow is working
- compact explanation style is working
- thumbs feedback is present
- redeem-code auth is implemented in backend and client
- secure token storage is implemented
- authenticated WebSocket flow is implemented
- hosted Supabase connectivity is working
- DB tables are reachable
- redeem codes are seeded in hosted DB
- Azure App Service backend is deployed
- hosted `/api/health` responds with `ok`
- hosted redeem-code login is verified
- hosted refresh flow is verified
- hosted logout and token revocation are verified
- hosted authenticated WebSocket explain flow is verified
- hosted DB logging is verified
- client staging and production config point to the hosted Azure backend
- client debug build succeeds
- client production publish succeeds
- tester bundle build succeeds
- sign-in UI has been polished
- packaging/support scripts are present

### Still not fully finished
- one full packaged-client login against the hosted backend is still pending
- one full packaged-client explanation request through real text selection and hotkey is still pending
- hosted DB verification from the packaged-client WPF flow is still pending
- packaged build still needs validation on a separate clean Windows machine

## 2. Remaining Work By Priority

## Priority A: Must Complete Before Pilot

### A1. Full packaged-client auth verification
Status: partly done

Work:
- test redeem-code login from the real WPF login UI
- test invalid code behavior
- test used code behavior
- test app restart with stored session
- test logout and refresh against hosted backend

Why it matters:
- the hosted auth endpoints are working, but pilot readiness still requires one complete real-client pass

Done when:
- sign-in, restart, refresh, and logout work against hosted backend from the real app

### A2. Full packaged-client explain verification
Status: partly done

Work:
- start packaged client against hosted backend
- redeem a real code in the real sign-in window
- select real text in a desktop app and press the hotkey
- verify streaming response returns correctly
- verify overlay remains responsive

Why it matters:
- this confirms the real packaged desktop path, not just direct hosted endpoint tests

Done when:
- one end-to-end hosted explanation succeeds from the real WPF client flow

### A3. Packaged-client DB request logging verification
Status: partly done

Work:
- confirm the new packaged-client `request_logs` row is created
- confirm the related `participants` row exists or is reused
- confirm the related `refresh_tokens` row exists
- confirm the related `sessions` row exists/updates
- verify final logged field values match expectations

Why it matters:
- direct hosted verification is already done, but the packaged-client path must also be proven for the study

Done when:
- one successful packaged-client request creates the expected rows in all related tables

## Priority B: Strongly Recommended Before External Users

### B1. Internal pilot validation
Status: not done

Work:
- test on 3 to 5 internal users
- cover VS Code, browser, Windows Terminal, classic terminal
- collect support bundles
- record auth issues, capture issues, and explanation quality issues

Done when:
- internal pilot completes without critical blockers

### B2. Packaging validation
Status: mostly done

Work:
- verify the published client on a clean Windows machine
- verify tester-bundle launch on a clean Windows machine
- confirm launcher works outside the dev machine
- confirm no dev-only files are required

Done when:
- a tester can run the packaged build without source-code setup

### B3. Support and operations flow
Status: partly done

Work:
- decide who receives tester issues
- decide where support bundles are sent
- define code issuance tracking
- define how pilot issues are triaged

Done when:
- support process is written and usable by the team

### B4. Privacy and tester guidance
Status: partly done

Work:
- finalize tester-facing privacy note
- clearly state that selected text and background context may be processed
- tell testers what they should not select

Done when:
- tester package includes clear privacy/use guidance

## Priority C: Quality and Hardening

### C1. Multi-environment QA
Status: not done

Work:
- Windows 10 and Windows 11 validation
- multiple DPI/scaling tests
- multiple monitor checks
- different browsers and terminals

### C2. Error handling validation
Status: partly done

Work:
- confirm backend-unavailable behavior
- confirm WebSocket disconnect behavior
- confirm token refresh failure path
- confirm sign-in recovery UX

### C3. Secret hygiene
Status: not done

Work:
- rotate any temporary API keys used during development
- ensure only final deployment secrets remain
- confirm no secrets are bundled into published client files

## 3. Detailed Verification Matrix

## Auth
- redeem code accepted
- invalid code rejected cleanly
- used code rejected cleanly
- refresh works
- logout revokes refresh token
- app restart restores session

## Streaming
- authenticated WebSocket connects
- first token arrives promptly
- complete message is received
- overlay status updates correctly

## Logging
- session id is created
- request id is unique
- final request row is written only once
- DB failure does not block user-visible output

## Feedback
- thumbs appear only when real response text is visible
- only one reaction per response
- reaction is logged without breaking the response flow

## 4. Suggested Execution Order

1. complete one manual packaged-client sign-in
2. complete one manual packaged-client hotkey explanation
3. verify the DB rows created by that packaged-client run
4. verify the packaged release on a clean Windows machine
5. internal pilot
6. fix pilot issues
7. external pilot

## 5. Deployment Blockers Right Now

These are the real blockers preventing a true pilot release today:
- no confirmed end-to-end live auth + explain + DB log test yet from the real packaged WPF client
- packaged build has not yet been validated on a clean tester machine

## 6. What Should Not Change Now

To keep risk low, we should not redesign these before the pilot:
- capture architecture
- overlay architecture
- WebSocket streaming architecture
- WPF client platform choice
- backend framework choice

## 7. Definition Of Done For Pilot Start

We can call the system pilot-ready when:
- hosted auth works
- hosted logging works
- packaged client works
- one packaged-client real sign-in and hotkey flow is verified
- internal users complete real tasks successfully
- no critical blocker remains in capture, overlay, or session flow
