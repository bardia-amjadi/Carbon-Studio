import './env.js';
import * as monaco from 'monaco-editor';
import { createBridge } from './bridge.js';

const webview = window.chrome && window.chrome.webview;
const post = (message) => webview.postMessage(message);

const editor = monaco.editor.create(document.getElementById('editor'), {
  model: null,
  automaticLayout: true,
  scrollBeyondLastLine: false,
  renderWhitespace: 'selection',
  dropIntoEditor: { enabled: false },
  // Monaco's own command palette (F1) is replaced by the application's palette.
  quickSuggestions: true,
});

const bridge = createBridge({
  monaco,
  editor,
  post,
  schedule: (fn, ms) => setTimeout(fn, ms),
  cancel: (handle) => clearTimeout(handle),
});

webview.addEventListener('message', (event) => bridge.handle(event.data));
window.addEventListener('keydown', (event) => bridge.handleKeyDown(event), true);
editor.addCommand(monaco.KeyCode.F1, () => post({ type: 'shortcut', chord: 'f1' }));

post({ type: 'ready' });
