const { startBridge } = require('../bridge');
const protocol = Number(process.argv[3] || 3);
startBridge(process.argv[2], selection => {
  const selected = selection || 'bridge_selection';
  return { selected_text: selected, background_context: `before\n${selected}\nafter` };
}, { protocol, ownerPid: Number(process.env.VSCODE_PID) }).then(bridge => {
  process.stdin.once('data', async () => { await bridge.dispose(); process.exit(0); });
  console.log('READY');
}).catch(() => process.exit(1));
