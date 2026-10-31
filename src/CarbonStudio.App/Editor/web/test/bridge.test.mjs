import test from 'node:test';
import assert from 'node:assert/strict';
import { createBridge, buildTheme, chordFor, ACTIONS } from '../src/bridge.js';

// ---- fakes -----------------------------------------------------------------------------------------------

function fakeModel(text, language) {
  const listeners = [];
  return {
    text, language, eol: null, disposed: false, options: null,
    getValue() { return this.text; },
    setEOL(eol) { this.eol = eol; },
    onDidChangeContent(fn) { listeners.push(fn); },
    updateOptions(o) { this.options = o; },
    dispose() { this.disposed = true; },
    edit(newText) { this.text = newText; listeners.forEach((fn) => fn({})); },
  };
}

function setup() {
  const posted = [];
  const timers = new Map();
  let nextTimer = 1;
  const monacoEditor = {
    themes: [], activeTheme: null, languages: [],
    EndOfLineSequence: { LF: 0, CRLF: 1 },
    EndOfLinePreference: { LF: 1 },
    createModel: (text, language) => fakeModel(text, language),
    defineTheme(name, def) { this.themes.push({ name, def }); },
    setTheme(name) { this.activeTheme = name; },
    setModelLanguage(model, language) { model.language = language; },
  };
  const cursorListeners = [];
  const actionsRun = [];
  const editor = {
    model: null, focused: 0, options: [], triggers: [], position: { lineNumber: 1, column: 1 },
    setModel(m) { this.model = m; },
    saveViewState() { return { model: this.model }; },
    restoreViewState(v) { this.restored = v; },
    focus() { this.focused += 1; },
    getPosition() { return this.position; },
    updateOptions(o) { this.options.push(o); },
    trigger(source, id) { this.triggers.push(id); },
    getAction(id) { return id === 'missing' ? null : { run() { actionsRun.push(id); } }; },
    onDidChangeCursorPosition(fn) { cursorListeners.push(fn); },
  };
  const bridge = createBridge({
    monaco: { editor: monacoEditor },
    editor,
    post: (m) => posted.push(m),
    schedule: (fn, ms) => { const id = nextTimer++; timers.set(id, { fn, ms }); return id; },
    cancel: (id) => timers.delete(id),
  });
  const runTimers = () => { const all = [...timers.values()]; timers.clear(); all.forEach((t) => t.fn()); };
  return { bridge, posted, editor, monacoEditor, timers, runTimers, cursorListeners, actionsRun };
}

const open = (ctx, id, text = 'hello', language = 'plaintext') => ctx.bridge.handle({ type: 'open', id, text, language });

// ---- tests -----------------------------------------------------------------------------------------------

test('open creates an LF model without activating it', () => {
  const ctx = setup();
  open(ctx, 'a', 'x\ny', 'csharp');
  assert.equal(ctx.bridge.documentCount, 1);
  assert.equal(ctx.editor.model, null);
  assert.equal(ctx.bridge.activeId, null);
});

test('activate swaps models, restores view state and reports the caret', () => {
  const ctx = setup();
  open(ctx, 'a'); open(ctx, 'b');
  ctx.bridge.handle({ type: 'activate', id: 'a', focus: true });
  assert.equal(ctx.bridge.activeId, 'a');
  assert.equal(ctx.editor.focused, 1);
  ctx.bridge.handle({ type: 'activate', id: 'b' });
  ctx.bridge.handle({ type: 'activate', id: 'a' });
  assert.ok(ctx.editor.restored, 'view state of a is restored');
  assert.deepEqual(ctx.posted.filter((m) => m.type === 'caret').at(-1), { type: 'caret', id: 'a', line: 1, column: 1 });
});

test('edits are debounced and posted with an increasing revision', () => {
  const ctx = setup();
  let captured;
  ctx.monacoEditor.createModel = (t, l) => (captured = fakeModel(t, l));
  open(ctx, 'a', 'one');
  assert.equal(ctx.timers.size, 0);

  captured.edit('two');
  captured.edit('three');
  assert.equal(ctx.timers.size, 1, 'second edit replaces the first timer');
  assert.equal(ctx.posted.filter((m) => m.type === 'text').length, 0);

  ctx.runTimers();
  assert.deepEqual(ctx.posted.filter((m) => m.type === 'text'), [{ type: 'text', id: 'a', rev: 1, text: 'three' }]);

  captured.edit('four');
  ctx.runTimers();
  assert.equal(ctx.posted.filter((m) => m.type === 'text').at(-1).rev, 2);
});

test('large documents use a longer debounce', () => {
  const ctx = setup();
  let captured;
  ctx.monacoEditor.createModel = (t, l) => (captured = fakeModel(t, l));
  open(ctx, 'a', 'x'.repeat(1_500_000));
  captured.edit('x'.repeat(1_500_001));
  assert.equal([...ctx.timers.values()][0].ms, 1000);
});

test('flush pushes every pending document immediately, then acknowledges', () => {
  const ctx = setup();
  const models = [];
  ctx.monacoEditor.createModel = (t, l) => { const m = fakeModel(t, l); models.push(m); return m; };
  open(ctx, 'a'); open(ctx, 'b'); open(ctx, 'c');
  models[0].edit('A!'); models[2].edit('C!');
  ctx.bridge.handle({ type: 'flush', requestId: 7 });
  const types = ctx.posted.map((m) => m.type);
  assert.deepEqual(types.slice(-3), ['text', 'text', 'flushed']);
  assert.equal(ctx.posted.at(-1).requestId, 7);
  assert.equal(ctx.timers.size, 0, 'timers cancelled');
  assert.deepEqual(ctx.posted.filter((m) => m.type === 'text').map((m) => m.id).sort(), ['a', 'c']);
});

test('flush with nothing pending still acknowledges', () => {
  const ctx = setup();
  ctx.bridge.handle({ type: 'flush', requestId: 1 });
  assert.deepEqual(ctx.posted, [{ type: 'flushed', requestId: 1 }]);
});

test('close disposes the model, cancels its timer and clears the active editor model', () => {
  const ctx = setup();
  let captured;
  ctx.monacoEditor.createModel = (t, l) => (captured = fakeModel(t, l));
  open(ctx, 'a');
  ctx.bridge.handle({ type: 'activate', id: 'a' });
  captured.edit('changed');
  ctx.bridge.handle({ type: 'close', id: 'a' });
  assert.equal(captured.disposed, true);
  assert.equal(ctx.editor.model, null);
  assert.equal(ctx.bridge.documentCount, 0);
  assert.equal(ctx.timers.size, 0);
});

test('re-opening an existing id replaces its model', () => {
  const ctx = setup();
  const models = [];
  ctx.monacoEditor.createModel = (t, l) => { const m = fakeModel(t, l); models.push(m); return m; };
  open(ctx, 'a', 'first'); open(ctx, 'a', 'second');
  assert.equal(models[0].disposed, true);
  assert.equal(ctx.bridge.documentCount, 1);
});

test('options update the editor and every model', () => {
  const ctx = setup();
  const models = [];
  ctx.monacoEditor.createModel = (t, l) => { const m = fakeModel(t, l); models.push(m); return m; };
  open(ctx, 'a');
  ctx.bridge.handle({ type: 'options', options: { fontFamily: 'Consolas', fontSize: 16, wordWrap: true, tabSize: 2, insertSpaces: false, minimap: true, lineNumbers: false } });
  assert.deepEqual(ctx.editor.options[0], { fontFamily: 'Consolas', fontSize: 16, wordWrap: 'on', minimap: { enabled: true }, lineNumbers: 'off' });
  assert.deepEqual(models[0].options, { tabSize: 2, insertSpaces: false });
});

test('theme message defines a fresh theme each time', () => {
  const ctx = setup();
  ctx.bridge.handle({ type: 'theme', dark: true, accent: '#FF8800' });
  ctx.bridge.handle({ type: 'theme', dark: false, accent: '#00AA55' });
  assert.deepEqual(ctx.monacoEditor.themes.map((t) => t.name), ['carbon-1', 'carbon-2']);
  assert.equal(ctx.monacoEditor.activeTheme, 'carbon-2');
  assert.equal(ctx.monacoEditor.themes[0].def.base, 'vs-dark');
  assert.equal(ctx.monacoEditor.themes[1].def.base, 'vs');
});

test('buildTheme falls back to the default accent for invalid colours', () => {
  assert.equal(buildTheme(true, 'red').colors['editorCursor.foreground'], '#38BDF8');
  assert.equal(buildTheme(false, '#123456').colors['editorCursor.foreground'], '#123456');
});

test('exec maps every known action and rejects unknown ones', () => {
  const ctx = setup();
  for (const name of Object.keys(ACTIONS)) {
    ctx.bridge.handle({ type: 'exec', action: name });
  }
  assert.deepEqual(ctx.editor.triggers, ['undo', 'redo']);
  assert.ok(ctx.actionsRun.includes('actions.find'));
  assert.ok(ctx.actionsRun.includes('editor.action.gotoLine'));
  assert.equal(ctx.posted.length, 0, 'no errors for known actions');

  ctx.bridge.handle({ type: 'exec', action: 'rm -rf' });
  assert.equal(ctx.posted.at(-1).type, 'error');
});

test('malformed and unknown messages are reported, never thrown', () => {
  const ctx = setup();
  ctx.bridge.handle(null);
  ctx.bridge.handle({ type: 'open', id: 5, text: 'x' });
  ctx.bridge.handle({ type: 'activate', id: 'missing' });
  ctx.bridge.handle({ type: 'nope' });
  assert.equal(ctx.posted.filter((m) => m.type === 'error').length, 4);
});

test('only chords requested by C# are intercepted', () => {
  const ctx = setup();
  const event = (init) => ({ key: 's', ctrlKey: false, shiftKey: false, altKey: false, prevented: 0, stopped: 0,
    preventDefault() { this.prevented++; }, stopPropagation() { this.stopped++; }, ...init });

  assert.equal(ctx.bridge.handleKeyDown(event({ ctrlKey: true })), false, 'nothing intercepted before shortcuts arrive');

  ctx.bridge.handle({ type: 'shortcuts', chords: ['ctrl+s', 'ctrl+shift+p', 42] });
  const save = event({ ctrlKey: true });
  assert.equal(ctx.bridge.handleKeyDown(save), true);
  assert.equal(save.prevented, 1);
  assert.equal(save.stopped, 1);
  assert.deepEqual(ctx.posted.at(-1), { type: 'shortcut', chord: 'ctrl+s' });

  assert.equal(ctx.bridge.handleKeyDown(event({ key: 'P', ctrlKey: true, shiftKey: true })), true);
  assert.equal(ctx.bridge.handleKeyDown(event({ key: 'z', ctrlKey: true })), false, 'ctrl+z stays with Monaco');
  assert.equal(ctx.bridge.handleKeyDown(event({ key: 'Control', ctrlKey: true })), false);
});

test('chordFor orders modifiers consistently', () => {
  assert.equal(chordFor({ key: 'S', ctrlKey: true, shiftKey: true, altKey: true }), 'ctrl+shift+alt+s');
  assert.equal(chordFor({ key: ',', ctrlKey: true }), 'ctrl+,');
  assert.equal(chordFor({ key: 'F1' }), 'f1');
  assert.equal(chordFor({ key: 'Shift', shiftKey: true }), null);
});

test('caret reports are coalesced', () => {
  const ctx = setup();
  open(ctx, 'a');
  ctx.bridge.handle({ type: 'activate', id: 'a' });
  const before = ctx.posted.length;
  ctx.cursorListeners[0]({ position: { lineNumber: 2, column: 3 } });
  ctx.cursorListeners[0]({ position: { lineNumber: 4, column: 5 } });
  assert.equal(ctx.posted.length, before);
  ctx.runTimers();
  assert.deepEqual(ctx.posted.at(-1), { type: 'caret', id: 'a', line: 4, column: 5 });
});
