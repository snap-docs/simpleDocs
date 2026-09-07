const { startBridge } = require('../bridge');
startBridge(process.argv[2], selection => ({
  selected_text: selection, background_context: `before\n${selection}\nafter`
})).then(bridge => {
  process.stdin.once('data', async () => { await bridge.dispose(); process.exit(0); });
  console.log('READY');
}).catch(() => process.exit(1));
