# Manual Final Test

Use this for the final practical desktop check before wider pilot use.

## What Is Already Verified

These checks are already done in the current project:

- Azure backend health works
- hosted redeem-code login works
- invalid redeem code returns a clean error
- already-used redeem code returns a clean error
- hosted refresh works
- hosted logout and token revocation work
- hosted authenticated WebSocket explain flow works
- hosted DB logging works
- feedback saves into `request_logs.feedback_reaction`
- packaged client builds successfully
- final tester zip builds successfully

## What You Still Need To Verify Manually

This is the final real-user desktop path:

1. unzip the final package on a clean Windows machine
2. sign in through the packaged WPF window if needed
3. select real text in a desktop app
4. press the hotkey
5. confirm the overlay visually
6. confirm the DB row created by that packaged-client path
7. confirm the app still works after a Windows restart

## Before You Start

1. Close any old `simpleDocs` or `CodeExplainer` process that is still open.
2. Use the latest package:
   `dist\simpleDocs-direct-exe-1.1.0-pilot.zip`
3. Extract it first.
4. Pick one redeem code that is still unused in `redeem_codes`.

## Sign-In Test

1. Open the extracted folder.
2. Run `app\CodeExplainer.exe`.
3. Wait for the sign-in window if the machine is not already signed in.
4. Type one unused redeem code.
5. Click `Continue`.

Expected result:

- the sign-in window closes
- the app stays running in the tray
- no error message is shown

If it fails:

- invalid code should say the code is invalid
- used code should say the code has already been used

## Explanation Test

Use a simple code sample first.

Example:

```ts
const items = [{ price: 5 }, { price: 10 }];
const total = items.reduce((sum, item) => sum + item.price, 0);
```

Steps:

1. Open VS Code or another supported app.
2. Highlight the `reduce(...)` line.
3. Press the configured hotkey.
4. Wait for the overlay to appear.

Expected result:

- overlay opens
- explanation streams in
- text is readable
- feedback controls appear only after response text is visible

## Feedback Test

1. After the response is visible, click thumbs up or thumbs down once.
2. Confirm the overlay accepts the click without breaking.

Expected result:

- the response stays visible
- no error popup appears
- `feedback_reaction` updates in the DB row for that request

## Restart Test

1. Close the app or restart Windows.
2. Sign back into Windows.
3. Confirm `simpleDocs` starts automatically in the tray.
4. If needed, right-click the tray icon and verify `Start On Windows Login` can be toggled.

Expected result:

- app starts in the tray after login
- hotkey works without reopening a main window

## DB Verification

After one successful packaged-client request, run these queries in Supabase.

Participants:

```sql
select * from participants order by created_at desc;
```

Refresh tokens:

```sql
select * from refresh_tokens order by created_at desc;
```

Request logs:

```sql
select request_id, task_type, status, feedback_reaction, timestamp, response_text
from request_logs
order by timestamp desc;
```

Expected result:

- a participant row exists or is reused
- a refresh token row exists
- a new request log row exists with a valid `status`
- the row can be updated with `feedback_reaction`

## Final Pass Criteria

The final desktop path is considered verified when:

- packaged sign-in succeeds
- the real hotkey flow shows the overlay
- the hosted DB gets the new request row
- thumbs feedback updates the request row
- Windows auto-start works as expected
- no critical UI issue blocks a tester from using the app
