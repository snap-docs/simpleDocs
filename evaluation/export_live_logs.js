import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const LOG_FILE = path.join(__dirname, '..', 'runlogs', 'client_live.log');
const OUTPUT_FILE = path.join(__dirname, 'live_evaluation_dataset.jsonl');

function parseLogs() {
  if (!fs.existsSync(LOG_FILE)) {
    console.error(`Log file not found: ${LOG_FILE}`);
    process.exit(1);
  }

  const logs = fs.readFileSync(LOG_FILE, 'utf-8').split('\n');
  const requests = new Map();

  for (const line of logs) {
    if (!line.trim()) continue;

    // Extract request ID if present (e.g. req=1)
    const reqMatch = line.match(/req=(\d+)/);
    const reqId = reqMatch ? parseInt(reqMatch[1], 10) : null;

    if (!reqId) continue;

    if (!requests.has(reqId)) {
      requests.set(reqId, {
        run_id: `case_${reqId}`,
        source: 'live_log',
        source_request_id: reqId,
        timestamp: null,
        category: null,
        environment_type: null,
        window_title: null,
        process_name: null,
        selected_method: null,
        background_method: null,
        selected_text: null,
        background_context: null,
        selected_text_is_preview: false,
        background_context_is_preview: false,
        selected_chars: 0,
        background_chars: 0,
        is_partial: false,
        is_unsupported: false,
        status_message: null,
        latency_ms: null,
        model_name: null,
        response: '',
        status: 'success' // Default to success, evaluate at end
      });
    }

    const data = requests.get(reqId);

    // Capture timestamp from first log
    if (!data.timestamp) {
        const timeMatch = line.match(/^\[(.*?)\]/);
        if (timeMatch) data.timestamp = timeMatch[1];
    }

    // Process specific properties based on log type
    if (line.includes('[Window]')) {
      const windowMatch = line.match(/Foreground process=([^\s]+) title="([^"]+)"/);
      if (windowMatch) {
        data.process_name = windowMatch[1];
        data.window_title = windowMatch[2];
      }
      const envMatch = line.match(/Classified environment as ([^\s.]+)/);
      if (envMatch) data.environment_type = envMatch[1];
    } 
    else if (line.includes('[CaptureSummary]')) {
      const summaryMatch = line.match(/status="([^"]+)"/);
      if (summaryMatch) data.status_message = summaryMatch[1];
      
      const latencyMatch = line.match(/duration_ms=(\d+)/);
      if (latencyMatch) data.latency_ms = parseInt(latencyMatch[1], 10);
    } 
    else if (line.includes('[Capture]')) {
      const envMethodMatch = line.match(/env=([^\s]+) method=([^\s]+).*?selected_chars=(\d+) visible_chars=(\d+).*?partial=(True|False) unsupported=(True|False)/);
      if (envMethodMatch) {
          data.environment_type = envMethodMatch[1];
          data.selected_method = envMethodMatch[2];
          data.selected_chars = parseInt(envMethodMatch[3], 10);
          data.background_chars = parseInt(envMethodMatch[4], 10);
          data.is_partial = envMethodMatch[5] === 'True';
          data.is_unsupported = envMethodMatch[6] === 'True';
      }

      const selectedMatchRegex = /Selected preview:\s*(.*)$/;
      const visibleMatchRegex = /Visible preview:\s*(.*)$/;

      const sMatch = line.match(selectedMatchRegex);
      if (sMatch) {
          data.selected_text = sMatch[1].trim();
          data.selected_text_is_preview = true;
          // In real log, if text is truncated it often ends with `...`
          if(!data.selected_text.endsWith('...')) {
              data.selected_text_is_preview = false; // It might be the full short text. This is a heuristic.
          }
      }

      const vMatch = line.match(visibleMatchRegex);
      if (vMatch) {
          data.background_context = vMatch[1].trim();
          data.background_context_is_preview = true;
          if(!data.background_context.endsWith('...')) {
            data.background_context_is_preview = false;
        }
      }
    } 
    // Wait for the actual token chunk contents instead of summary
    // Since backend stream log lines don't print token content locally in client log in prototype3
    // But evaluating the token chunks tells us there was a response
    else if (line.includes('[Backend]')) {
        const errMatch = line.match(/error="([^"]+)"/i);
        if (errMatch) {
            data.status_message = `Backend error: ${errMatch[1]}`;
            data.status = 'backend_failure';
        }
    }
    // E.g [Clipboard] Capture failed: Data on clipboard is invalid (0x800401D3 (CLIPBRD_E_BAD_DATA))
    else if (line.includes('ERROR [Clipboard]')) {
        data.status_message = line.substring(line.indexOf('ERROR [Clipboard]') + 18).trim();
    }
  }

  // Final evaluation of generated records
  const outputData = [];
  for (const [id, reqData] of requests.entries()) {
      if (reqData.is_unsupported) {
          reqData.status = 'capture_failure';
      } else if (reqData.is_partial) {
          reqData.status = 'partial';
      }
      
      // If we completely failed to grab text
      if (reqData.selected_chars === 0 && reqData.background_chars === 0) {
          reqData.status = 'capture_failure';
      }

      // Convert empty strings back to null
      if (reqData.response === '') reqData.response = null;

      outputData.push(reqData);
  }

  // Write out as JSONL
  if (fs.existsSync(OUTPUT_FILE)) {
      fs.unlinkSync(OUTPUT_FILE); // Wipe old
  }
  
  for (const item of outputData) {
      fs.appendFileSync(OUTPUT_FILE, JSON.stringify(item) + '\n');
  }

  console.log(`Exported ${outputData.length} records to ${OUTPUT_FILE}`);
}

parseLogs();
