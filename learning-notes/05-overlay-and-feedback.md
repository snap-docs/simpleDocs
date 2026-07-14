# Overlay And Feedback

The overlay is the user's main visible experience. It appears near the mouse, shows a loading state, streams tokens as they arrive, formats the final response, and lets the user send thumbs feedback.

The main file is `client/OverlayWindow.xaml.cs`, with layout in `client/OverlayWindow.xaml`.

## Overlay Lifecycle

The overlay starts hidden. `App.xaml.cs` creates it during startup and keeps it available for hotkey requests.

The main public methods are:

- `ShowLoading`
- `AppendToken`
- `SetStatus`
- `OnStreamComplete`
- `ShowMessage`

`ShowLoading` resets state for a new request, stores the current request id, clears old response text, updates the status label, hides feedback, and positions the overlay near the mouse pointer.

The overlay clamps its position to the working area so it does not open beyond the screen edge.

## Streaming Display

`BackendClient` receives WebSocket messages and calls overlay callbacks on the UI thread.

The first token hides the loading panel and shows the content scroller. Each token is appended to `ResponseText`, and the scroller moves to the end.

This gives the user immediate feedback. They do not wait for the whole AI response before seeing useful content.

## UI Thread Safety

WPF UI elements must be accessed on the dispatcher thread. Overlay methods call `Dispatcher.Invoke` so background network callbacks can safely update UI.

`BackendClient` also has `RunOnUiThread` for callback delivery.

This is one of the most important desktop concepts in the project: network and capture work can be asynchronous, but UI mutation belongs to the UI dispatcher.

## Compact Formatting

The model is asked to return short labeled lines like:

```text
Definition: This declares a constant.
Purpose: It stores the value used later.
Effect: The later console call prints that value.
```

After streaming completes, `ApplyCompactColorFormatting` rebuilds the text as WPF `Run` elements.

`TryExtractTitleLabel` looks for a short label before a colon. Labels get color based on meaning:

- issue/error/problem labels use error color
- hint/check/warning labels use warning color
- builtin/framework/annotation labels use a distinct accent
- done/success labels use success color
- default labels use title color

This is why prompt design and overlay formatting are linked. The model's label style gives the overlay something simple to color without needing markdown parsing.

## Dismiss Behavior

The overlay can be dismissed with Escape or by clicking outside feedback controls.

Right-click drag moves the window. That lets the user reposition it without adding a full window chrome.

The overlay does not act like a normal main application window. It is a temporary helper surface.

## Feedback UI

Thumbs feedback is hidden until a real response is visible. It is not shown for unsupported capture messages or empty state messages.

The overlay tracks:

- `_currentRequestId`
- `_feedbackSubmitted`
- `_isResponseVisible`
- `_selectedReaction`

When the user clicks thumbs up or down, `SubmitFeedbackAsync`:

1. prevents duplicate submission
2. visually marks the selected reaction
3. logs the feedback event locally
4. calls the injected `FeedbackHandler`
5. re-enables feedback if storage fails

The `FeedbackHandler` is provided by `App.xaml.cs` and calls the backend through `BackendClient.SendFeedbackAsync`.

## Backend Feedback Route

`backend/src/routes/feedback.js` validates:

- JSON body exists
- `request_id` is present
- reaction is `up` or `down`

It reads the authenticated user from Hono context, resolves the participant id, and calls `saveRequestFeedback`.

`saveRequestFeedback` updates `request_logs.feedback_reaction` where both `request_id` and `participant_id` match. This prevents one participant from updating another participant's row.

## Why Feedback Updates Existing Logs

Feedback is tied to a completed explanation. The request row is created after the stream finishes. Later, the thumbs action updates that same row.

This keeps analysis simple:

- one request row
- one response text
- one optional reaction

The app does not need a separate feedback table unless feedback becomes more complex later.

## Failure Behavior

Feedback failure does not crash the app. If storage fails, the overlay resets the selected reaction and lets the user try again.

This behavior is appropriate because feedback is useful but not core to the explanation flow. The user should not lose the explanation because feedback failed.
