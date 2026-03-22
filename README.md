# Code Explainer — Comprehensive Project Documentation

Welcome to the Code Explainer project. This document serves as the complete technical blueprint and onboarding guide for all team members. It details every aspect of the system's architecture, underlying theory, technical implementation, and constraints. **Read this document thoroughly before contributing to the codebase.**

---

## SECTION 1 — PROJECT OVERVIEW

Code Explainer is a Windows desktop floating assistant engineered to deliver instant, contextual, plain-English explanations of confusing code, error messages, terminal output, and technical jargon. 

The core user interaction is designed for absolute minimal friction:
1. The developer selects any text on their screen within any application (editors, browsers, terminals).
2. The developer presses the global OS-level hotkey: **`Ctrl+Space`**.
3. A transparent, topmost floating widget appears near the cursor.
4. An AI-generated explanation streams into the widget, character by character, with the first token visible in under 2 seconds.
5. The developer reads the explanation and dismisses the widget with a single click or the `Escape` key, never having left their current application context.

**The Most Critical Design Rule (The "Hint-Only" Constraint):**
When the system detects that the user has highlighted an error message or stack trace, the AI is strictly constrained to provide a **directional hint only.** It will *never* provide a complete solution, it will *never* provide corrected code, and it will *never* show the developer the exact fix. 

This rule is absolute and exists to enforce cognitive engagement, grounded in the theory of Productive Failure (Kapur, 2010). Supplying complete answers bypasses the learning process; supplying hints forces the developer to engage in effortful reasoning, resulting in stronger knowledge retention and skill acquisition.

The system natively handles and classifies four types of content: source code spanning any language, error messages/stack traces, terminal commands/output, and general technical text.

---

## SECTION 2 — WHY THIS PROJECT EXISTS

The primary problem this tool solves is the severe cognitive cost of context switching during software development. 

Modern developers frequently encounter confusing code or errors. To resolve them, they highlight the text, switch windows to a browser, navigate to an AI tool or forum, paste the text, and read the answer. Studies show developers switch to a browser 10 to 20 times per session. According to Mark et al. (2008), each context switch costs an average of 23 minutes of cognitive recovery time to return to the original state of deep focus. 

Existing tools fail to solve this problem effectively:
* **GitHub Copilot Chat** is locked inside specific IDE environments (like VS Code or Visual Studio) and cannot help when the developer is reading documentation in Chrome or debugging in an external Windows Terminal.
* **ChatGPT / Claude Web Interfaces** require a hard context switch to a web browser, breaking flow state.
* **All existing market tools** default to providing complete, copy-pasteable solutions. While this provides immediate gratification, it bypasses the learning process, leading to a phenomenon where junior developers rely entirely on the AI without understanding the underlying systems.

This project addresses a specific research gap: There is currently no existing tool that combines system-wide OS-level operation (functioning across any app natively), a single low-friction hotkey trigger, automatic text classification, and pedagogically motivated "hint-only" guidance for errors.

---

## SECTION 3 — COMPLETE TECH STACK WITH REASONS

Every technology in this stack was chosen to prioritize native OS integration, speed, and pedagogical constraints over theoretical ecosystem purity.

* **C# .NET 8 (Windows Presentation Foundation - WPF)**
  * *What it is:* Microsoft's primary framework for building native Windows desktop applications.
  * *What it does:* Powers the entire client-side application, UI capture, global hotkey registration, and the floating overlay widget.
  * *Why it was chosen:* The primary requirement of this app is reading text from other windows. The Microsoft `UIAutomationClient` library is built natively into .NET. We explicitly avoided Node.js/Electron because the Chrome sandbox blocks native UI Automation. We avoided Tauri (Rust) because UIA bindings in Rust are notoriously unstable. We avoided Python because `uiautomation` fails to read text inside Electron-based apps (like VS Code). C# is the mathematically proven, single reliable option for stable Windows UI Automation.
  
* **WPF (Windows Presentation Foundation)**
  * *What it is:* A UI framework within .NET used to render desktop interfaces.
  * *What it does:* Renders the floating, transparent, dark-themed glassmorphic overlay widget that displays the AI streaming text.
  * *Why it was chosen:* WPF natively allows for `Topmost` rendering, complex transparency (`AllowsTransparency="True"`), and borderless window styles (`WindowStyle="None"`) with zero web-engine overhead. It allows us to build a visually modern overlay without shipping an embedded Chromium instance.

* **Node.js with Hono**
  * *What it is:* A JavaScript runtime paired with Hono, an ultrafast web framework.
  * *What it does:* Powers the backend API, handles request validation, executes the classification engine, formats prompts, and streams output via WebSockets.
  * *Why it was chosen:* Node.js has historically excellent asynchronous streaming support. The actual bottleneck in any LLM application is the AI model's Time-to-First-Token (TTFT) and generation speed, not the backend CPU execution time. Thus, native, reliable WebSocket streaming quality matters significantly more than raw computational speed. Hono is lightweight and optimized for the Edge/Node.

* **OpenRouter**
  * *What it is:* A unified API routing gateway for Large Language Models.
  * *What it does:* Acts as the singular integration point for AI generation, accepting prompts and yielding SSE (Server-Sent Events) streams.
  * *Why it was chosen:* OpenRouter prevents vendor lock-in. It allows the team to switch the underlying AI model by changing exactly one environment variable. It also provides free tier access to powerful models for development. 
  * *Primary Model:* `meta-llama/llama-3.3-70b-instruct:free`
  * *Fallback Model:* `google/gemini-2.0-flash-exp:free`

* **Supabase PostgreSQL**
  * *What it is:* An open-source Firebase alternative powered by a PostgreSQL database.
  * *What it does:* Logs anonymous interaction metadata.
  * *Why it was chosen:* To track actual usage metrics and system performance (response times, fallback usage) without writing custom database layers. **Critically, it stores anonymous metadata only. It never stores actual parsed code, private keys, or the actual text payloads.** Tracked fields include: `timestamp`, `case_type`, `response_time_ms`, `model_used`, and `fallback_triggered`.

* **Supabase Auth with JWT**
  * *What it is:* JSON Web Token based authentication provided by Supabase.
  * *What it does:* Validates incoming requests to the Node.js backend.
  * *Why it was chosen:* API cost protection. Because OpenRouter calls cost money (on non-free tiers), the API must be protected against malicious hammering. JWT auth enables hard rate-limiting per user. (Note: Auth can be bypassed for local development using `SKIP_AUTH=true`).

* **Railway.app**
  * *What it is:* A modern cloud hosting platform.
  * *What it does:* Hosts the Node.js backend.
  * *Why it was chosen:* It offers zero-configuration continuous deployment from GitHub and provisions automatic SSL/TLS certificates, which are strictly required for secure WebSockets (`wss://`).

---

## SECTION 4 — HOW SELECTED TEXT IS CAPTURED

This mechanism is the core technical achievement of the C# client. It dictates how text is pulled from external applications silently.

**What is Windows UI Automation (UIA)?**
The Windows UI Automation API is an accessibility framework originally built by Microsoft for screen readers (like Narrator) to assist visually impaired users. Because screen readers must be able to "read" the text inside any application UI, UIA provides deep programmatic access to the Document Object Model of the Windows OS, exposing text content, bounding rectangles, and UI states of almost any element in any application.

**Exact Technical Steps:**
1. The user presses `Ctrl+Space`.
2. The code invokes `AutomationElement.FocusedElement` to acquire the exact UI element the user's text cursor is currently interacting with.
3. The system queries this element for the `IUIAutomationTextPattern` (the specific UIA interface that governs text ranges).
4. The system executes `GetSelection()` on the TextPattern, which returns an array of text ranges representing what the user has currently highlighted with their mouse.
5. The system executes `GetText(-1)` upon that range array, returning the raw highlighted string.

**The Integrity of UIA:**
This process reads directly from the target application's memory space via the OS accessibility bridge. 
- There is no clipboard interaction.
- There is no keyboard macro simulation.
- There is no file reading from the local disk.
- There are no side effects left behind. 
The process is completely silent, completely invisible, and conceptually identical to how a screen reader operates.

**Implementation File:** `client/UIATextReader.cs` using the `System.Windows.Automation` framework namespace.

---

## SECTION 5 — HOW FULL FILE CONTEXT IS CAPTURED

Merely understanding the 10 lines of code a user highlighted is often insufficient for an LLM to provide a highly accurate explanation. Full context is required so the AI can see variable declarations, imports, and architectural patterns surrounding the highlighted snippet.

**Exact Technical Steps:**
1. Utilizing the exact same `FocusedElement` acquired during the selected text capture phase.
2. Utilizing the exact same `IUIAutomationTextPattern`.
3. The system executes `DocumentRange`, which returns a single text range representing the absolute entirety of the visible text document currently loaded inside the UI element.
4. The system executes `GetText()` on that document range. 
5. To prevent catastrophic memory bloat or massive token-usage costs, this string is hard-capped at 10,000 characters.

**Integrity of Context Capture:**
Like selected text, this context capture reads directly from application memory. 
- There is no disk access. 
- The system does not attempt to search the physical hard drive for the file path.
- There are no privacy issues regarding scanning the user's broader file system; it only reads what is actively loaded into the specific UI element's read-buffer.

---

## SECTION 6 — FALLBACK WHEN UIA FAILS

UI Automation is not universally supported. Applications that utilize custom GUI rendering engines bypass the standard Windows accessibility trees. 
- Terminal applications (`cmd.exe`, `powershell.exe`, `windowsterminal.exe`) do not expose standard UIA `TextPattern` interfaces.
- Mozilla Firefox has a historically unreliable implementation of UIA bridges.

For these applications, the system executes an automated, invisible **Clipboard Fallback**.

**Exact Steps of Clipboard Fallback (`ClipboardFallback.cs`):**
1. **Save:** The system queries the OS clipboard. If text exists, it is saved into a local C# memory variable. The system clipboard is then temporarily cleared.
2. **Select All:** The system utilizes `InputSimulator` to inject the physical keystrokes `Ctrl+A` into the OS, highlighting the entire visible window.
3. **Copy:** The system injects `Ctrl+C` to copy the text.
4. **Read:** The system reads the newly populated clipboard to acquire the `full_context`.
5. **Undo:** The system injects `Ctrl+Z` to undo the selection highlighting in the editor.
6. **Restore:** The system takes the string saved in Step 1 and writes it back to the system clipboard.
7. **Result:** The developer's clipboard is mathematically identical to exactly how it was before `Ctrl+Space` was pressed. The process is fully automatic and visually imperceptible.

**Terminal Special Case:**
If the user executes the fallback inside a Terminal application, injecting `Ctrl+C` is catastrophic — it sends a `SIGINT` (Interrupt Signal) that will forcefully terminate whatever script or server the developer currently has running. Therefore, for terminals exclusively, the fallback injects `Ctrl+A` to select all, and then injects the `Enter` key (which is the native "Copy" command in Windows terminals), completely mitigating the `SIGINT` risk.

**OCR (Optical Character Recognition) Rejection:**
During architectural prototyping, rendering the screen to an image and running OCR (Tesseract) was evaluated as a fallback mechanism. It was permanently rejected. The computation latency was too high, violently defeating the strict under 2-seconds response-time target constraint.

---

## SECTION 7 — HOW GLOBAL HOTKEY WORKS

The trigger mechanism must fire instantaneously, regardless of which application the developer is working inside. Local keyboard hooks are insufficient as they require the WPF application to be in active focus.

The system utilizes P/Invoke (Platform Invocation Services) to reach outside the .NET runtime and call native unmanaged Win32 APIs from `user32.dll`. 
The system calls `RegisterHotKey(hWnd, id, fsModifiers, vk)` specifying `MOD_CONTROL` and `VK_SPACE`. 

Because WPF applications do not expose a standard Win32 message loop window by default, `HotkeyManager.cs` utilizes an `HwndSource` hook to tap directly into the Windows Message (WM) pump. When the OS detects `Ctrl+Space`, it broadcasts `WM_HOTKEY` to the message pump. The `HwndSource` hook catches this integer, confirms the ID matches our registration, and fires a standard C# Event (`HotkeyPressed`), propagating the action safely back into managed .NET code.

---

## SECTION 8 — HOW APPLICATION DETECTION WORKS

To route logic correctly (e.g., deciding whether to attempt UIA or immediately default to Clipboard Fallback to save time), the system must know exactly what application the developer is using.

**Implementation File:** `client/AppDetector.cs`

**Execution Flow:**
1. `GetForegroundWindow()` (Win32 API) is called to retrieve the memory Handle (HWND) of whatever window currently has focus in the OS.
2. `GetWindowThreadProcessId()` (Win32 API) is called, passing the Handle, to retrieve the OS-level Process ID (PID).
3. The .NET wrapper `Process.GetProcessById(pid)` evaluates the PID to retrieve the exact executable string name (e.g., `code`).

**App Classifications:**
The process string is checked against hardcoded dictionaries:
- **Editors** (`editor`): `code.exe`, `cursor.exe`, `sublime_text.exe`, `devenv.exe`, etc. (Primarily target UIA).
- **Browsers** (`browser`): `chrome.exe`, `msedge.exe`. (Target UIA). `firefox.exe` is a browser but maps strictly to UIA-skip.
- **Terminals** (`terminal`): `cmd.exe`, `powershell.exe`, `windowsterminal.exe`. (Strictly mapped to skip UIA entirely and execute Terminal-Safe Clipboard Fallback).
- **Unknown**: If an app is unrecognized, the system gracefully attempts UIA, catches any UIA crash/failure, and proceeds to standard Clipboard Fallback.

---

## SECTION 9 — 4 CASE CLASSIFICATION

When the Node.js backend receives a payload, it does not blindly forward text to the LLM. It passes the text through `classifier.js`, a regex-based pattern matching engine that determines the user's intent with absolute certainty and zero user input.

* **Case 1: Source Code**
  * *Trigger:* Detects standard programming syntax constructs (`function`, `class`, `def`, brackets, statement terminators).
  * *AI Directive:* The LLM explains what the code does line-by-line, but critically, it is instructed to explicitly identify and NAME the underlying programming concept (e.g., "This utilizes *recursion*", "This is an example of *Closures*"). The goal is to teach transferable knowledge, not just describe a localized sequence of operations.

* **Case 2: Error Messages / Stack Traces**
  * *Trigger:* Detects keywords (`Error`, `Exception`, `Traceback`, `FATAL`) or regex signatures matching stack trace frame formats.
  * *Priority:* **Error classification is absolute.** If the user highlights a single error message physically appended to 1,000 lines of standard source code, the system will discard the code classification and forcefully designate the entire payload as Case 2.
  * *AI Directive:* **THE MOST CRITICAL CONSTRAINT IN THE SYSTEM.** The AI explains what the error literally means and why it occurred. It gives exactly ONE directional hint. It is explicitly forbidden by multiple prompt constraints from ever providing the corrected code or the complete solution.

* **Case 3: Terminal Content**
  * *Trigger:* Detects shell prompts (`$`, `>`, `C:\Users>`) or known CLI commands (`git`, `npm`, `docker`).
  * *AI Directive:* Terminal commands (user input) and terminal output (bash response) are conceptually treated as a unified Case 3 entity. They are never separated. The AI explains the overall meaning, breaks down what specific flags (`-f`, `--global`) execute, and states clearly if human intervention/action is required to proceed.

* **Case 4: General Technical Text**
  * *Trigger:* If the text fails to generate enough confidence scores to match Cases 1, 2, or 3. (Usually READMEs, Jira tickets, or generic documentation).
  * *AI Directive:* Absolute brevity. The LLM must return the explanation in exactly ONE plain English sentence. No markdown headers, no bullet points, no elaboration.

---

## SECTION 10 — PEDAGOGICAL FRAMEWORK

The behavioral constraints placed upon the AI are not arbitrary. They are strictly researched interpretations of established cognitive science and learning methodologies.

**1. Kapur's Productive Failure (2010)**
This theory posits that allowing learners to struggle and generate effortful reasoning toward a solution produces vastly stronger long-term retention and conceptual understanding than simply handing them the correct answer. This is the sole justification for the strict **"Hint Only" constraint for Case 2 errors.** Existing AI tools act as "answer engines" that facilitate passive receipt of code. Code Explainer acts as a cognitive mentor, forcing the developer to solve the localized problem while providing the necessary guardrails.

**2. Lave and Wenger's Situated Learning (1991)**
This theory dictates that learning is optimized when it occurs exactly at the moment, and within the exact context, where the knowledge is required. By intercepting text inside the IDE and projecting the floating overlay natively over the developer's code, we construct a Situated Learning environment, eliminating the contextual degradation that occurs when switching to an external web browser.

**3. Sweller's Cognitive Load Theory (1988)**
Working memory is highly limited. Programming imposes a high "intrinsic" cognitive load on novices. When a tool forces a user to navigate complex multi-tab UIs, switch applications, and formulate complex prompt engineering, it adds "extraneous" cognitive load, overflowing the working memory capacity and preventing learning. This justifies the single `Ctrl+Space` interaction paradigm, the zero context-switching, and the short, plain-English response vectors.

---

## SECTION 11 — COMPLETE FOLDER STRUCTURE

```
CodeExplainer/
│
├── client/                     (C# .NET 8 WPF Application)
│   ├── CodeExplainer.csproj    WPF Project configuration and NuGet references (InputSimulator).
│   ├── App.xaml / .cs          Entry point. Constructs the system tray icon, orchestrates the entire capture->send flow.
│   ├── MainWindow.xaml / .cs   Hidden window. Exists solely to provide an HWND handle for the Win32 message pump hook.
│   ├── HotkeyManager.cs        P/Invoke hooks to user32.dll RegisterHotKey. Captures Ctrl+Space globally.
│   ├── AppDetector.cs          P/Invoke hooks to GetForegroundWindow. Maps PID to application classification strings.
│   ├── UIATextReader.cs        Interfaces with UIAutomationClient to extract selected text and 10k max full_context.
│   ├── ClipboardFallback.cs    Memory buffers clipboard, injects input sequences, restores clipboard for specific apps.
│   ├── BackendClient.cs        Executes the initial HTTP POST request and upgrades connection to WebSocket for stream parsing.
│   └── OverlayWindow.xaml / .cs Translates XAML glassmorphism. Manages the TextBlock UI thread for appending streaming tokens.
│
├── backend/                    (Node.js Hono Server)
│   ├── package.json            Dependencies: hono, @supabase/supabase-js, jsonwebtoken.
│   ├── index.js                Application server root. Bootstraps the Hono instance and binds to the specified port.
│   ├── .env                    Secret keys (GitIgnored).
│   ├── .env.example            Template showing required variables.
│   ├── src/
│   │   ├── app.js              Hono factory. Binds CORS, applies JWT middleware, routes REST and WebSocket endpoints.
│   │   ├── routes/
│   │   │   ├── health.js       GET /api/health - Uptime and ping validation.
│   │   │   └── explain.js      POST /api/explain - Parent orchestration route for classification and Supabase async logging.
│   │   ├── services/
│   │   │   ├── classifier.js   Regex mapping arrays. Evaluates scoring logic for Case 1-4.
│   │   │   ├── promptEngine.js Dictionary of Case-specific system prompts containing pedagogical restrictions.
│   │   │   ├── openRouterClient.js  Fetch API wrapper. Translates OpenRouter SSE chunks into an async generator yield queue.
│   │   │   └── streamHandler.js     Iterates over the async generator, pushing WebSocket text frames to the C# client.
│   │   ├── middleware/
│   │   │   ├── auth.js         JWT decryption. Allows bypass via SKIP_AUTH env flag.
│   │   │   └── validate.js     Sanitizes JSON inputs, enforce character length limits, drops unprintable characters.
│   │   ├── db/
│   │   │   └── supabase.js     Supabase client execution. Inserts anonymous logging payloads. No-ops securely on failure.
│   │   └── utils/
│   │       └── logger.js       Internal formatted console logger.
```

---

## SECTION 12 — COMPLETE SYSTEM FLOW

1. **Trigger:** Developer presses `Ctrl+Space` anywhere in the Windows OS.
2. **Hook Catch:** `HotkeyManager.cs` intercepts the `WM_HOTKEY` code via `HwndSource` and fires the managed C# event.
3. **App Detection:** `AppDetector.cs` queries the OS handle, identifying the active process (e.g., `code.exe`).
4. **Capture Decision:** The client checks if the app is UIA-supported or blacklisted (e.g., `cmd.exe`).
5. **Primary Capture:** If UIA-supported, `UIATextReader.cs` queries `FocusedElement` for selection bounds and document text.
6. **Fallback Capture:** If UIA fails or is blacklisted, `ClipboardFallback.cs` buffers the real clipboard, injects keystrokes, steals text, and restores the real clipboard.
7. **HTTP POST:** `BackendClient.cs` sends a JSON payload (`selected_text`, `full_context`, `app_type`) to the Node.js API.
8. **Auth Validation:** `auth.js` middleware validates the JWT token (if `SKIP_AUTH` is false).
9. **Payload Sanitation:** `validate.js` ensures text lengths are safe and formats are correct.
10. **Classification:** `classifier.js` analyzes the text and computes regex scores, determining the Case (1-4).
11. **Prompt Compilation:** `promptEngine.js` wraps the payload in the Case-specific pedagogical instruction template.
12. **AI Execution:** `openRouterClient.js` POSTs to the OpenRouter API requesting a streaming response.
13. **WebSocket Relay:** The C# client upgrades to a WebSocket. `streamHandler.js` yields SSE tokens, pushing them character-by-character over the WS tunnel.
14. **Overlay Render:** `OverlayWindow.xaml.cs` catches tokens on the UI Thread, dismissing the loading spinner and rendering text into the floating glass UI.
15. **Telemetry:** `explain.js` fire-and-forgets a logging payload to Supabase Tracking containing anonymous performance timings.

---

## SECTION 13 — ENVIRONMENT VARIABLES

Located in `backend/.env`.

| Variable | Description | Where to get it |
|----------|-------------|-----------------|
| `PORT` | Local port the Node server binds to. | Default: `3000` |
| `OPENROUTER_API_KEY` | Authentication key for OpenRouter AI routing. | [openrouter.ai/keys](https://openrouter.ai/keys) |
| `OPENROUTER_MODEL` | The string identifier of the primary model to use. | Setup: `meta-llama/llama-3.3-70b-instruct:free` |
| `OPENROUTER_FALLBACK_MODEL` | String identifier if primary goes down. | Setup: `google/gemini-2.0-flash-exp:free` |
| `SUPABASE_URL` | The REST API endpoint of your logging database. | Supabase Dashboard -> Project Settings -> API |
| `SUPABASE_ANON_KEY` | The public anonymous key for inserts. | Supabase Dashboard -> Project Settings -> API |
| `SUPABASE_JWT_SECRET` | Secret used to sign/verify authentication tokens. | Supabase Dashboard -> Project Settings -> API |
| `SKIP_AUTH` | Boolean string. Set to `true` to disable JWT checks. | Type `true` for local development. |

---

## SECTION 14 — HOW TO RUN LOCALLY

### Phase 1: Start the Backend (Node.js)
1. Ensure Node.js v18+ is installed.
2. Open a terminal and navigate to the backend directory:
   ```bash
   cd d:/PROJECTS/Startup/prototype1/backend
   ```
3. Install the NPM packages:
   ```bash
   npm install
   ```
4. Copy `.env.example` to `.env` and insert your OpenRouter API Key. Ensure `SKIP_AUTH=true` is set.
5. Start the development server with live-reloading:
   ```bash
   npm run dev
   ```
6. **Verify:** Open `http://localhost:3000/api/health` in your browser. It must respond with `{"status":"ok"}`.

### Phase 2: Start the Client (C# .NET)
1. Ensure the .NET 8.0 SDK is installed on your Windows machine.
2. Open a new terminal and navigate to the client directory:
   ```bash
   cd d:/PROJECTS/Startup/prototype1/client
   ```
3. Compile and launch the application:
   ```bash
   dotnet run
   ```
4. **Verify:** You should see a white `i` (Information) icon appear in your Windows System Tray (bottom right of taskbar). No main window will appear.

### Phase 3: Total System Test
1. Open Visual Studio Code, Chrome, or Windows Terminal.
2. Highlight a section of text or code with your mouse.
3. Without changing windows, press **`Ctrl+Space`**.
4. **Verify:** The dark glass overlay window should materialize near your mouse cursor. A loading spinner will appear briefly, followed by streaming text from the AI.
5. Press `Escape` to close the overlay.

---

## SECTION 15 — RESEARCH CONTEXT

Code Explainer is actively developed for submission to the **IEEE IES Global Competition 2026**. 
- **Submission Deadline:** April 15, 2026.
- **Platform Limitations:** Windows-only capability is rigorously justified by market data indicating that over 70% of professional software developers natively utilize Windows environments. macOS support requires deep architectural rewrites using Swift/Accessibility APIs and is explicitly identified in documentation as "Future Work".

**Evaluation Plan Methodology:**
Success metrics for the IEEE submission are computed via two distinct evaluative tracks:
1. **Human Evaluation (Qualitative):** A Google Form survey will be distributed to 35 real-world developers utilizing the finalized deployment. Metrics collected will include Likert-scale usability ratings and a formal System Usability Scale (SUS) score to mathematically prove UI minimization success.
2. **LLM Judge Evaluation (Quantitative):** The development team will compile a rigorous testing payload of 50 diverse input strings (Code, Errors, Junk text). This payload will be processed by the system. A highly capable secondary "Judge LLM" (e.g., GPT-4) will ingest the results and score responses on Accuracy, Clarity, Concept Identification, and **Hint Compliance**. *Hint Compliance for Case 2 (Errors) is the singular most critical statistical metric required to prove that the pedagogical "Hint Only" constraint operates successfully at scale.*

---

## SECTION 16 — KEY RULES THAT MUST NEVER BE VIOLATED

As a contributor, you are subject to the following absolute system laws. Pull requests violating these rules will be rejected violently.

1. **NEVER READ FILES FROM DISK:** The C# client must parse memory strictly via UI Automation or active Clipboard arrays. We do not incur disk I/O latency, and we do not scan developer hard drives.
2. **NEVER STORE ACTUAL CODE IN DATABASE:** The Supabase instance logs completely anonymous metadata integers and timestamps. It must never store user payload strings, fulfilling zero-trust privacy mandates.
3. **NEVER CAPTURE WITHOUT EXPLICIT TRIGGER:** The system does not "watch" the screen passively. It executes capture strictly and only when the `WM_HOTKEY` intercepts the physical execution of `Ctrl+Space`.
4. **ALWAYS RESTORE THE CLIPBOARD:** If UIA fails and the Clipboard Fallback executes, the original contents of the user's system clipboard must be restored verbatim. Leaving the developer's clipboard in a modified state is destructive UX.
5. **ERROR CLASSIFICATION ALWAYS WINS:** If the classification logic detects both source code and error messages in the same payload, Case 2 (Error) takes supreme priority to prevent the AI from bypassing constraints.
6. **CASE 2 MEANS HINTS ONLY:** The AI shall never write corrected code, nor supply a complete solution for an error trace. 
7. **SUB-2 SECOND RESPONSE TIME:** The time delta between striking `Ctrl+Space` and the display of the first streamed character token in the WPF overlay must not exceed 2.0 seconds. 
8. **WINDOWS ONLY:** Do not commit cross-platform libraries intended for macOS. This is a strictly integrated Win32 mechanism package.
9. **NO OCR EVER:** Optical Character Recognition is permanently banned from the architecture due to unacceptable compute latency. 
