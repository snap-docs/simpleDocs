# Live Log Export Tool Implementation Plan

## Goal Description
Create a lightweight, separate Node.js script that parses `runlogs/client_live.log` (live request logs) and converts each request into a simple structured JSON (or JSONL) dataset suitable for evaluation, model comparison, and paper analysis. The script must not modify the live application, affect runtime performance, or alter capture logic.

## User Review Required
> [!IMPORTANT]
> Confirm the desired output file location and name (e.g., `evaluation/live_evaluation_dataset.jsonl`).
> Confirm whether to output JSONL (one record per line) or a single JSON array. The plan assumes JSONL for streaming simplicity.

## Proposed Changes
---
### New Script
- **[NEW] [export_live_logs.js](file:///d:/PROJECTS/Startup/prototype4%20-%20Copy%20%282%29/evaluation/export_live_logs.js)**
  - Reads `runlogs/client_live.log`.
  - Splits log into request blocks using regex for `req=\d+` or `Flow` entries.
  - Extracts fields:
    - `run_id` (generated UUID or `case_{req}`)
    - `source` = "live_log"
    - `source_request_id` (numeric request ID)
    - `timestamp` (first log entry timestamp for the request)
    - `environment_type` (from `[Capture]` line)
    - `window_title` and `process_name` (from `[Window]` line)
    - `selected_method` and `background_method` (from `[Capture]` line)
    - `selected_text` (from `Selected preview:` line, if present)
    - `background_context` (from `Visible preview:` line, if present)
    - `selected_text_is_preview` / `background_context_is_preview` (boolean flags)
    - `selected_chars` (selected_chars value)
    - `background_chars` (visible_chars value)
    - `is_partial`, `is_unsupported` (from capture line)
    - `status_message` (from `[CaptureSummary]` line)
    - `latency_ms` (duration_ms from capture line)
    - `model_name` (null – not present in logs)
    - `response` (concatenated token contents from `[Backend] Token received` lines)
    - `status` (derived: `success` if not partial/unsupported, else `partial`, `capture_failure`, or `backend_failure` based on flags and token presence)
  - Handles incomplete logs by setting missing fields to `null` and marking appropriate status.
  - Writes each record as a line of JSON to `evaluation/live_evaluation_dataset.jsonl`.

---
## Open Questions
> [!IMPORTANT]
> - Do you prefer the output file to be placed in `evaluation/` or another directory?
> - Should the script be executable via `node export_live_logs.js` or added as an npm script entry?

## Verification Plan
- Run the script locally and inspect the first few lines of the generated JSONL.
- Ensure that fields match the log content and that missing data results in `null`.
- Verify that the script does not modify any existing files.
