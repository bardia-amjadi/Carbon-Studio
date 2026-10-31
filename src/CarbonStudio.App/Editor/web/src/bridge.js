// Message bridge between Carbon Studio (C#) and a Monaco editor instance.
//
// All logic lives here, behind an injected `monaco`/`editor`/`post`, so it can be unit tested in node with
// fakes (test/bridge.test.mjs). main.js wires the real Monaco and WebView2 objects.
//
// Protocol (JSON objects, `type` selects the message). Must stay in sync with
// src/CarbonStudio.Core/Editor/EditorProtocol.cs.
//
//   C# -> JS: open, activate, deactivate, close, language, options, theme, shortcuts, exec, flush, focus
//   JS -> C#: ready, text, caret, shortcut, flushed, error

const SMALL_DOCUMENT_DELAY_MS = 200;
const LARGE_DOCUMENT_DELAY_MS = 1000;
const LARGE_DOCUMENT_CHARS = 1_000_000;
const CARET_DELAY_MS = 30;

/** Monaco action ids for the actions C# may request. */
export const ACTIONS = Object.freeze({
  undo: { trigger: 'undo' },
  redo: { trigger: 'redo' },
  cut: { action: 'editor.action.clipboardCutAction' },
  copy: { action: 'editor.action.clipboardCopyAction' },
  paste: { action: 'editor.action.clipboardPasteAction' },
  selectAll: { action: 'editor.action.selectAll' },
  find: { action: 'actions.find' },
  replace: { action: 'editor.action.startFindReplace' },
  goToLine: { action: 'editor.action.gotoLine' },
  formatDocument: { action: 'editor.action.formatDocument' },
  trimTrailingWhitespace: { action: 'editor.action.trimTrailingWhitespace' },
});

/** Builds a Monaco theme definition matching the application palette. Pure; exported for tests. */
export function buildTheme(dark, accent) {
  const accentColor = /^#[0-9a-fA-F]{6}$/.test(accent) ? accent : '#38BDF8';
  return dark
    ? {
        base: 'vs-dark',
        inherit: true,
        rules: [],
        colors: {
          'editor.background': '#10161D',
          'editor.foreground': '#E6EDF3',
          'editorLineNumber.foreground': '#5B6876',
          'editorLineNumber.activeForeground': '#B7C3CF',
          'editor.lineHighlightBackground': '#161E27',
          'editorCursor.foreground': accentColor,
          'editor.selectionBackground': accentColor + '44',
          'editorWidget.background': '#141A21',
          'editorWidget.border': '#232C36',
        },
      }
    : {
        base: 'vs',
        inherit: true,
        rules: [],
        colors: {
          'editor.background': '#FFFFFF',
          'editor.foreground': '#1B232B',
          'editorLineNumber.foreground': '#9AA6B2',
          'editorLineNumber.activeForeground': '#3B4652',
          'editor.lineHighlightBackground': '#F1F5F9',
          'editorCursor.foreground': accentColor,
          'editor.selectionBackground': accentColor + '44',
          'editorWidget.background': '#F8FAFC',
          'editorWidget.border': '#D3DAE1',
        },
      };
}

/** "ctrl+shift+p" style chord for a keyboard event, or null for events that are only modifiers. */
export function chordFor(event) {
  const key = typeof event.key === 'string' ? event.key.toLowerCase() : '';
  if (!key || key === 'control' || key === 'shift' || key === 'alt' || key === 'meta') {
    return null;
  }

  const parts = [];
  if (event.ctrlKey) parts.push('ctrl');
  if (event.shiftKey) parts.push('shift');
  if (event.altKey) parts.push('alt');
  parts.push(key);
  return parts.join('+');
}

export function createBridge({ monaco, editor, post, schedule, cancel }) {
  /** @type {Map<string, {model: any, viewState: any, rev: number, timer: any, pending: boolean}>} */
  const documents = new Map();
  let activeId = null;
  let caretTimer = null;
  let interceptedChords = new Set();
  let themeDefinitionCount = 0;

  const report = (message) => post({ type: 'error', message: String(message) });

  function delayFor(entry) {
    return entry.model.getValue().length > LARGE_DOCUMENT_CHARS ? LARGE_DOCUMENT_DELAY_MS : SMALL_DOCUMENT_DELAY_MS;
  }

  function pushText(id, entry) {
    if (entry.timer !== null) {
      cancel(entry.timer);
      entry.timer = null;
    }
    if (!entry.pending) {
      return;
    }
    entry.pending = false;
    entry.rev += 1;
    // Documents use '\n' internally; the file's real line ending is applied by C# when saving.
    post({ type: 'text', id, rev: entry.rev, text: entry.model.getValue(monaco.editor.EndOfLinePreference.LF) });
  }

  function open(msg) {
    if (typeof msg.id !== 'string' || typeof msg.text !== 'string') {
      throw new Error('open requires string id and text');
    }
    if (documents.has(msg.id)) {
      close({ id: msg.id });
    }

    const model = monaco.editor.createModel(msg.text, msg.language || 'plaintext');
    model.setEOL(monaco.editor.EndOfLineSequence.LF);
    const entry = { model, viewState: null, rev: 0, timer: null, pending: false };
    model.onDidChangeContent(() => {
      entry.pending = true;
      if (entry.timer !== null) {
        cancel(entry.timer);
      }
      entry.timer = schedule(() => pushText(msg.id, entry), delayFor(entry));
    });
    documents.set(msg.id, entry);
  }

  function activate(msg) {
    const entry = documents.get(msg.id);
    if (!entry) {
      throw new Error('activate: unknown document ' + msg.id);
    }
    if (activeId !== null && documents.has(activeId)) {
      documents.get(activeId).viewState = editor.saveViewState();
    }
    activeId = msg.id;
    editor.setModel(entry.model);
    if (entry.viewState) {
      editor.restoreViewState(entry.viewState);
    }
    if (msg.focus) {
      editor.focus();
    }
    const position = editor.getPosition ? editor.getPosition() : null;
    if (position) {
      post({ type: 'caret', id: msg.id, line: position.lineNumber, column: position.column });
    }
  }

  function close(msg) {
    const entry = documents.get(msg.id);
    if (!entry) {
      return;
    }
    if (entry.timer !== null) {
      cancel(entry.timer);
    }
    if (activeId === msg.id) {
      editor.setModel(null);
      activeId = null;
    }
    entry.model.dispose();
    documents.delete(msg.id);
  }

  function applyOptions(options) {
    const o = options || {};
    editor.updateOptions({
      fontFamily: o.fontFamily,
      fontSize: o.fontSize,
      wordWrap: o.wordWrap ? 'on' : 'off',
      minimap: { enabled: !!o.minimap },
      lineNumbers: o.lineNumbers === false ? 'off' : 'on',
    });
    for (const entry of documents.values()) {
      entry.model.updateOptions({ tabSize: o.tabSize, insertSpaces: o.insertSpaces });
    }
    // Models created later pick the options up from the editor defaults.
    editor.updateOptions({ tabSize: o.tabSize, insertSpaces: o.insertSpaces });
  }

  function applyTheme(msg) {
    // Each definition gets a fresh name so Monaco re-applies it even when only the accent changed.
    const name = 'carbon-' + ++themeDefinitionCount;
    monaco.editor.defineTheme(name, buildTheme(!!msg.dark, msg.accent));
    monaco.editor.setTheme(name);
  }

  function exec(msg) {
    const spec = ACTIONS[msg.action];
    if (!spec) {
      throw new Error('exec: unknown action ' + msg.action);
    }
    editor.focus();
    if (spec.trigger) {
      editor.trigger('carbon', spec.trigger, null);
      return;
    }
    const action = editor.getAction(spec.action);
    if (!action) {
      throw new Error('exec: Monaco has no action ' + spec.action);
    }
    action.run();
  }

  function flush(msg) {
    for (const [id, entry] of documents) {
      pushText(id, entry);
    }
    post({ type: 'flushed', requestId: msg.requestId });
  }

  function handle(msg) {
    try {
      switch (msg && msg.type) {
        case 'open': open(msg); break;
        case 'activate': activate(msg); break;
        case 'deactivate':
          if (activeId !== null && documents.has(activeId)) {
            documents.get(activeId).viewState = editor.saveViewState();
          }
          activeId = null;
          editor.setModel(null);
          break;
        case 'close': close(msg); break;
        case 'language': {
          const entry = documents.get(msg.id);
          if (entry) monaco.editor.setModelLanguage(entry.model, msg.language || 'plaintext');
          break;
        }
        case 'options': applyOptions(msg.options); break;
        case 'theme': applyTheme(msg); break;
        case 'shortcuts':
          interceptedChords = new Set(Array.isArray(msg.chords) ? msg.chords.filter((c) => typeof c === 'string') : []);
          break;
        case 'exec': exec(msg); break;
        case 'flush': flush(msg); break;
        case 'focus': editor.focus(); break;
        default: report('unknown message type: ' + (msg && msg.type));
      }
    } catch (error) {
      report(error && error.message ? error.message : error);
    }
  }

  /**
   * Keys pressed while the web view has focus never reach WinUI accelerators, so the chords C# asked for
   * are forwarded as `shortcut` messages and swallowed here. Returns true when the event was intercepted.
   */
  function handleKeyDown(event) {
    const chord = chordFor(event);
    if (chord === null || !interceptedChords.has(chord)) {
      return false;
    }
    event.preventDefault();
    event.stopPropagation();
    post({ type: 'shortcut', chord });
    return true;
  }

  editor.onDidChangeCursorPosition((e) => {
    if (activeId === null) {
      return;
    }
    if (caretTimer !== null) {
      cancel(caretTimer);
    }
    const id = activeId;
    caretTimer = schedule(() => {
      caretTimer = null;
      post({ type: 'caret', id, line: e.position.lineNumber, column: e.position.column });
    }, CARET_DELAY_MS);
  });

  return { handle, handleKeyDown, get activeId() { return activeId; }, get documentCount() { return documents.size; } };
}
