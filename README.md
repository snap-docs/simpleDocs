# Intelligent Context Engine & Code Explainer (Team Onboarding Guide)

Welcome to the Prototype 4 repository. This document serves as the absolute master guide for onboarding new engineers to the system. It covers the full architecture, exact technologies utilized, the context-capture flow, evaluation systems, and the future evolution of our OS-level assistant.

Prototype 4 is an advanced, Windows-first OS-level assistant that explains highlighted code, browser text, and terminal streams instantaneously—without forcing developers to switch windows. 

By leveraging native UI scraping APIs, the system understands exactly what you highlight, identifies the application you are in, seamlessly extracts the surrounding background visual context natively, and beams it through WebSockets to a local node routing backend to generate context-aware LLM answers.

---

## 1. High-Level System Architecture

The ecosystem is decoupled into three primary workspaces carefully curated for performance, safety, and testing isolation.

### The Stack
- **Desktop Client Engine**: C# / .NET 8 / WPF (Orchestrator + Capture Pipeline)
- **OS Interop Libraries**: Windows UI Automation (UIA) / MSAA / Global Keyboard Hooks / Win32
- **Hardware Fallbacks**: `Windows.Media.Ocr.OcrEngine` (Native UWP Visual Scanner)
- **Backend Service**: Node.js / Hono Web Framework / WebSockets
- **AI Generation**: OpenRouter API / Groq API (`llama-3.3-70b-instruct:free` recommended)
- **Persistence Layer**: Local `.jsonl` Database for Evaluation Results.

### Repository Structure

```text
/client
  /Engine              # Core Universal Context Capture System
    /Strategies        # Target strategies (Browser, Classic Console, Clipboard Compatibility)
    /Classifiers       # Regex heuristics to parse IDE, Terminal, Web environment states
    /Detectors         # Probes inspecting active foreground Win32 window processes
    /Models            # Immutable domain results for payload streaming
  MainWindow.xaml      # Invisible background input orchestrator
  OverlayWindow.xaml   # Auto-positioning UI for LLM Markdown rendering
  BackendClient.cs     # Real-time WebSocket transmission

/backend
  /src
    /routes            # Isolated REST API bounds + health pings
    /services          # LLM Prompt generation (`promptEngine.js`) & WebSocket chunk router (`streamHandler.js`)
    /middleware        # Auth & validation boundaries
    app.js             # Hono WebSocket upgrade orchestration
  .env                 # Private Keys

/evaluation            # ⚗️ Isolated Lab Workspace (For Team Testing without breaking Prod)
  /cases               # Pre-configured `.json` datasets targeting edge cases
  /results             # Output `.jsonl` streams (Latencies, Accuracy, Strategy Routes)
  eval_runner.js       # Headless test script interacting directly with the Backend WS
```

---

## 2. The Context Capture Engine (Deep Dive for Engineers)

This is the most complex logic inside the C# client. When a user presses `Ctrl+Shift+Space`, the client performs a microscopic inspection of the foreground application state in milliseconds.

### Phase A: Detection & Classification
1. **Window Probing**: Win32 APIs (`GetForegroundWindow`, `GetWindowText`, `GetClassName`) detect the active process.
2. **Environment Classification**: The app routes the active hook to an environment token via regex:
   - `ide_editor` (VS Code, Visual Studio, Cursor)
   - `ide_embedded_terminal` (Interpreting embedded consoles)
   - `browser_chromium` / `browser_firefox`
   - `modern_terminal` (Windows Terminal) / `classic_terminal` (cmd.exe)

### Phase B: Selected Text Extraction Technologies
Depending on the classified environment, the system utilizes a cascading ladder of technologies to extract exactly what the user highlighted.
1. **Windows UI Automation (UIA) `TextPattern`**: Our primary extractor. Used to seamlessly rip text strings directly out of native elements cleanly.
2. **MSAA / IAccessible**: The legacy COM-based hierarchy. We use this as a secondary check if UIA is unpopulated.
3. **Clipboard Compatibility Mode**: Used instantly for IDEs (like VS Code) which disable accessibility plugins by default. The system securely saves the user's active clipboard, emulates a safe `Ctrl+C` keyboard block directly to the active process, scrapes the bytes, and restores the user's clipboard history instantly with zero loss.
4. **Classic Console Buffer API**: Uses `AttachConsole` + `ReadConsoleOutputCharacter` to natively tap into legacy CMD windows safely.

### Phase C: Background Context Extraction Technologies
Background context provides the AI with the variables, classes, and UI structures surrounding the highlighted text. We extract this passively at high speeds:
1. **UIA Document/Visible Ranges & Tree Containers**: We crawl the background structure tree natively and serialize it into background strings.
2. **The Ultimate Fail-safe - Native Windows OCR**: 
   - *Problem*: WebGL, Flash, Remote Desktops, or Google Docs utilizing Canvas rendering intentionally hide DOM elements to block screen scrapers. 
   - *Solution*: We invoke `Windows.Media.Ocr.OcrEngine` (native Windows 10/11 engine). The bot takes a sub-second visual capture of the active bounds and reads pixels identically to how a human eye works. This allows the system to scrape background data out of *literally any* application securely.

---

## 3. Client & Backend Protocol Contract

When the C# Client determines it has valid context, it opens a WebSocket to `ws://localhost:3000/ws/stream` and dumps the `CaptureResult` payload:

```json
{
  "selected_text": "class items(BaseModel): name...",
  "background_context": "from pydantic import BaseModel...",
  "window_title": "requestBodyValidation.py - Visual Studio Code",
  "process_name": "Code",
  "environment_type": "ide_editor",
  "selected_method": "clipboard_compatibility",
  "background_method": "uia_textpattern_visible_ranges",
  "is_partial": false,
  "is_unsupported": false
}
```

The Node.js backend streams chunked tokens right back for parsing.

---

## 4. Full Evaluation & Testing Plan

To maintain production stability without bloating the C# app, all model latency and fallback routing assessments happen entirely inside the `/evaluation` workspace using a Headless Mock Client (`eval_runner.js`).

### How the Local Evaluation Workflow Acts
1. **Isolate Logic**: Evaluative testing never touches C#. Instead, it directly contacts the normal Node.js WebSocket route `ws://localhost:3000/ws/stream`.
2. **Dummy Injectors**: Predefined JSON blocks replicating exactly what the UI *would* scrape exist under `/evaluation/cases/`. This lets engineers debug prompt responses accurately without having to open an IDE manually.
3. **Automated Logging**: The Node Script streams the AI's tokens natively to memory and stores a flat data dump to `/results/eval_results.jsonl`, generating highly readable rows of tracking data: Input Context, Output Speed (MS), Final Generation, Case Types.

### Running an Evaluation Test:
You must ensure the backend server is running first (refer to section 6), as the evaluator tests the live generation paths.
```powershell
cd evaluation
npm run eval
```
The script will loop through all .json files inside /cases/, wait for final response chunks over WS, and print successes/latencies to the terminal before saving quietly inside /results.

**For a deep dive into the latest benchmarking results (95.5% browser success rate, 1.7s latency) and tool operational guides, see:**
👉 [Evaluation Module & Performance Report](evaluation/evaluation_module_details.md)

---

## 5. Future Engineering Roadmap

We have major structural plans queued to construct long-term memory frameworks:

- **Semantic Cross-Session Embeddings (Graph Memories):**
  Every captured frame will eventually trigger background mathematical vector extractions via an Embedding model. By storing these vectors locally, we compute cosine similarity against incoming queries. If the user highlights a "Null Reference Variable" in a Terminal crash log, the AI will pull the matching "Controller Function" the user was examining in their IDE 12 minutes earlier—generating cross-application "Struggle Patterns" to give the AI infinite local context.

---

## 6. Full Running Guide (Basic Developer Setup)

Create `backend/.env` from `backend/.env.example`.

Minimum required values:
```env
PORT=3000
SKIP_AUTH=true
AI_PROVIDER=openrouter
OPENROUTER_API_KEY=your_openrouter_api_key_here
OPENROUTER_MODEL=meta-llama/llama-3.3-70b-instruct:free
```

### Option A: Clean One-Click Launch (Recommended)
Use the included helper scripts to safely clear zombie node processes and boot ports reliably:

```powershell
cd d:\PROJECTS\Startup\prototype4
.\run.ps1
```
(*Or simply double-click `run.bat` at root. This spins up the invisible API backend in port 3000 and the native UI in separate channels.*)

### Option B: Manual Separated Boot 
**Terminal 1 (Backend - The Brain):**
```powershell
cd backend
npm install
npm run dev
```

**Terminal 2 (Client - The Window/UI Hook):**
```powershell
cd client
dotnet run
```
