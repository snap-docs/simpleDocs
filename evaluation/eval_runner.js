import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
import WebSocket from 'ws';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const RESULTS_DIR = path.join(__dirname, 'results');
const RESULTS_FILE = path.join(RESULTS_DIR, 'eval_results.jsonl');
const WS_URL = 'ws://localhost:3000/ws/stream';

// Ensure results directory exists
if (!fs.existsSync(RESULTS_DIR)) {
  fs.mkdirSync(RESULTS_DIR, { recursive: true });
}

async function runTestCase(caseData) {
  return new Promise((resolve) => {
    const runId = caseData.run_id || caseData.case_id || 'unknown';
    console.log(`\n▶ Running: ${runId}`);

    const ws = new WebSocket(WS_URL);
    const startTime = Date.now();
    let responseText = '';
    let finalStatus = 'success';
    let modelName = caseData.model_name || null;

    ws.on('open', () => {
      // Send only the fields the backend expects
      const payload = {
        selected_text:      caseData.selected_text,
        background_context: caseData.background_context,
        window_title:       caseData.window_title,
        process_name:       caseData.process_name,
        environment_type:   caseData.environment_type,
        selected_method:    caseData.selected_method,
        background_method:  caseData.background_method,
        is_partial:         caseData.is_partial,
        is_unsupported:     caseData.is_unsupported
      };
      ws.send(JSON.stringify(payload));
    });

    ws.on('message', (message) => {
      try {
        const msg = JSON.parse(message.toString());
        if (msg.type === 'token') {
          responseText += msg.content;
          process.stdout.write(msg.content);
        } else if (msg.type === 'meta') {
          // Capture model name if backend echoes it
          if (msg.model) modelName = msg.model;
        } else if (msg.type === 'error') {
          finalStatus = 'backend_failure';
          responseText += `[ERROR: ${msg.message}]`;
          console.error(`\n❌ Error: ${msg.message}`);
        } else if (msg.type === 'complete') {
          ws.close();
        }
      } catch (e) {
        console.error(`Failed to parse message: ${e}`);
        finalStatus = 'backend_failure';
      }
    });

    ws.on('close', () => {
      const latencyMs = Date.now() - startTime;
      console.log(`\n✓ Done in ${latencyMs}ms`);

      // Derive status
      let status = finalStatus;
      if (status === 'success') {
        if (caseData.is_unsupported) status = 'capture_failure';
        else if (caseData.is_partial)  status = 'partial';
      }

      const result = {
        run_id:                       runId,
        source:                       caseData.source || 'manual',
        source_request_id:            caseData.source_request_id || null,
        timestamp:                    caseData.timestamp || null,
        category:                     caseData.category || null,
        environment_type:             caseData.environment_type || null,
        window_title:                 caseData.window_title || null,
        process_name:                 caseData.process_name || null,
        selected_method:              caseData.selected_method || null,
        background_method:            caseData.background_method || null,
        selected_text:                caseData.selected_text || null,
        background_context:           caseData.background_context || null,
        selected_text_is_preview:     caseData.selected_text_is_preview ?? false,
        background_context_is_preview: caseData.background_context_is_preview ?? false,
        selected_chars:               caseData.selected_chars ?? (caseData.selected_text?.length || 0),
        background_chars:             caseData.background_chars ?? (caseData.background_context?.length || 0),
        is_partial:                   caseData.is_partial,
        is_unsupported:               caseData.is_unsupported,
        status_message:               caseData.status_message || null,
        latency_ms:                   latencyMs,
        model_name:                   modelName,
        response:                     responseText || null,
        status
      };

      fs.appendFileSync(RESULTS_FILE, JSON.stringify(result) + '\n');
      resolve();
    });

    ws.on('error', (err) => {
      console.error(`\n❌ Connection Error: ${err.message}`);
      const latencyMs = Date.now() - startTime;
      const result = {
        run_id:            runId,
        source:            caseData.source || 'manual',
        source_request_id: caseData.source_request_id || null,
        timestamp:         caseData.timestamp || null,
        latency_ms:        latencyMs,
        model_name:        null,
        response:          null,
        status:            'backend_failure',
        status_message:    err.message
      };
      fs.appendFileSync(RESULTS_FILE, JSON.stringify(result) + '\n');
      resolve();
    });
  });
}

async function main() {
  console.log('Starting Evaluation Runner...');
  console.log(`Results → ${RESULTS_FILE}\n`);

  const casesFile = path.join(__dirname, 'all_cases.json');
  if (!fs.existsSync(casesFile)) {
    console.error('all_cases.json not found.');
    process.exit(1);
  }

  const allCases = JSON.parse(fs.readFileSync(casesFile, 'utf-8'));
  console.log(`Loaded ${allCases.length} test case(s).`);

  for (const caseData of allCases) {
    await runTestCase(caseData);
  }

  console.log(`\n🎉 Evaluation complete! Results saved to:\n  ${RESULTS_FILE}`);
}

main().catch(console.error);
