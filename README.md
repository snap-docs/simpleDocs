# Code Explainer — Floating Assistant

Code Explainer is a Windows desktop floating assistant that delivers instant, plain-English explanations of confusing code, error messages, and terminal content. 

Developers can select any text on their screen, access a global hotkey (**Ctrl+Space**), and instantly receive context-aware, AI-powered explanations dynamically streamed to a fully transparent floating overlay widget.

## 🏗️ Architecture & Tech Stack

The architecture cleanly divides native OS integration from the AI processing engine.

### Client Side (Windows Desktop)
- **Framework**: C# .NET 8 (WPF) — chosen for native accessibility hooks; no Electron or Tauri overhead.
- **UI Element Analysis**: `UIAutomationClient` (Microsoft UI Automation API)
- **Global Input Hook**: Win32 API (`RegisterHotKey`)

### Backend Side (Node.js API)
- **Framework**: Hono.js (running on Node)
- **Streaming**: Native WebSockets for character-by-character token delivery
- **AI Integration**: OpenRouter API (configurable model, defaults to Anthropic Claude 3.5 Haiku)
- **Database/Logging**: Supabase PostgreSQL (via Supabase JS client)
- **Authentication**: JWT verification middleware

## 🧠 Core Features & Logic

Code Explainer automatically intercepts the active application context using a tiered capture strategy, classifies the developer's intent, and restricts AI behaviors based on pedagogical principles (like Productive Failure).

### 1. Smart Context Capture (Client-Side)
The C# client determines what the developer is looking at through two fallback stages:
* **Primary (UIA)**: Uses the `UIAutomationClient` to silently pull both the currently highlighted text AND the full document text (up to 10k characters) without touching the system clipboard.
* **Secondary (Clipboard Fallback)**: For applications where UIA fails (like Terminal or Firefox), it saves the current clipboard, simulates `Ctrl+A`/`Ctrl+C` via keyboard injection, reads the text, simulates `Ctrl+Z`, and restores the original user clipboard invisibly.

### 2. Four-Tier Classification System (Backend-Side)
The backend intercepts incoming requests and automatically routes them to designated AI prompt instructions based on the text contents:
1. **Case 1 (Source Code)**: Identifies programming keywords. The AI is instructed to explain the core programming concept (e.g., closures, inheritance).
2. **Case 2 (Error Messages)**: *Highest Priority.* Identifies stack traces and error codes. **The AI is strictly constrained to give a directional hint only. It will never provide the corrected code or a complete solution.**
3. **Case 3 (Terminal Output)**: Identifies shell prompts and commands. Explains flags and expected actions.
4. **Case 4 (General Text/Docs)**: Explains jargon in exactly *one* plain English sentence.

## 📂 Project Structure

```text
CodeExplainer/
├── client/                     # C# .NET 8 WPF Application
│   ├── App.xaml/.cs            # Hidden tray app orchestration
│   ├── HotkeyManager.cs        # Win32 global hotkey registration
│   ├── UIATextReader.cs        # Primary text capture via UIAutomation
│   ├── ClipboardFallback.cs    # Secondary text capture via InputSimulator
│   ├── AppDetector.cs          # Identify foreground process (Code, Chrome, Terminal)
│   ├── BackendClient.cs        # HTTP requests and WebSocket streaming
│   └── OverlayWindow.xaml/.cs  # Transparent, floating glassmorphic UI
│
└── backend/                    # Node.js Hono Server
    ├── index.js                # Server entry point
    ├── src/
    │   ├── app.js              # Hono app and WebSocket setup
    │   ├── routes/             # POST /explain and GET /health
    │   ├── services/
    │   │   ├── classifier.js   # Regex-based 4-case classification engine
    │   │   ├── promptEngine.js # System prompts with Case 2 strict constraints
    │   │   ├── openRouter...js # SSE API client
    │   │   └── streamHandler.js# Yield tokens to WS
    │   ├── middleware/         # JWT Auth and Request validation
    │   └── db/supabase.js      # Anonymous metadata logging
    └── .env                    # Secrets config
```

## 🚀 How to Run Locally

### Prerequisites
1. **.NET 8 SDK** (for the Client)
2. **Node.js 18+** (for the Backend)
3. **OpenRouter API Key** (for AI generation)

### 1. Start the Backend
Navigate to the `backend` folder, install packages, and supply your environment variables.

```bash
cd backend
npm install
```

Copy `.env.example` to `.env` and fill in your keys:
* `OPENROUTER_API_KEY`: Required.
* `SKIP_AUTH`: Set to `true` to test locally without Supabase JWTs.

Start the development server:
```bash
npm run dev
```
*The server will start on `http://localhost:3000`.*

### 2. Start the Client
Open a new terminal, navigate to the `client` folder, and run the C# project.

```bash
cd client
dotnet run
```
*The application runs silently in the background. You'll see a white info icon appear in your Windows System Tray.*

### 3. Test the Flow
1. Open any application (e.g., VS Code, a Browser, or Command Prompt).
2. Highlight some text (try an error message!).
3. Press **`Ctrl+Space`**.
4. The dark glass floating overlay will appear near your mouse and stream the explanation. Click anywhere or press `Esc` to dismiss it.
