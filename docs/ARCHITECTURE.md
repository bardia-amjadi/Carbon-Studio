# Architecture (through Phase 2)

## Layers

```
CarbonStudio.App (WinUI 3, net10.0-windows)      UI only: windows, controls, dialogs, pickers, theming
        |  depends on
CarbonStudio.Core (net10.0, no UI references)    services, models, persistence, logging
```

`Core` never references WinUI. Everything with behaviour worth testing lives there and is covered by
`tests/CarbonStudio.Core.Tests`; `App` contains the glue that turns Core state into pixels.

## Core modules

| Namespace | Responsibility |
|---|---|
| `Paths` | `AppPaths` (per-user data locations), `PathGuard` (inside-root checks, name validation), `AtomicFile` (temp-then-replace writes) |
| `Settings` | `AppSettings` model, `SettingsService` (layered), `SettingsValidator` (clamps and repairs values) |
| `Workspace` | `WorkspaceService` (current folder), `ProjectDetector` (marker-file detection), `RecentProjectsStore`, `WorkspaceSummary` |
| `FileSystem` | `FileSystemService` (workspace-confined create/rename/delete/list), `TextFile` (BOM/encoding/EOL detection and round-tripping) |
| `Documents` | `DocumentManager` + `DocumentSession` (open files, dirty state, save, rename tracking), `TextPosition` |
| `Editor` | `LanguageCatalog` (file name to editor language), `EditorProtocol` (validated JSON messages with the web editor), `EditorAction`, `ShortcutMap` (key chords derived from command shortcut hints) |
| `Commands` | `CommandRegistry` and `CommandPaletteSearch` (fuzzy ranking); commands can declare themselves unavailable with a reason |
| `Logging`, `Output` | Rolling file logger; in-memory output channels shown in the Output panel and a logger that mirrors Information+ into them |
| `DependencyInjection` | `AddCarbonCore` composes everything |

## Settings precedence

Lowest to highest: **built-in defaults**, **user** (`%LOCALAPPDATA%\CarbonStudio\settings.json`),
**workspace** (`<workspace>/.carbon/settings.json`). Layers are deep-merged as JSON: objects merge, arrays and scalars replace,
`null` means "no override". Files may be partial. A file that is not valid settings (bad JSON, unknown enum value) is reported in
`ISettingsService.LoadWarnings`, user files are moved aside as `settings.json.corrupt-<timestamp>` (never deleted), and defaults are used.
Values are clamped by `SettingsValidator`. Workspace settings come from the opened folder and are therefore untrusted input;
later phases must not let them define executable behaviour without user approval.

## Threading

Core services use `ConfigureAwait(false)`, so their events (`Settings.Changed`, `Workspace.Changed`) can fire on thread-pool threads;
the UI marshals them with `DispatcherQueue`. `DocumentManager` is the exception: it holds unsynchronised state and must be used from
the UI thread, so it deliberately resumes on the caller's context and raises events there.

## UI structure

`MainWindow` is split into partial classes by concern: `Editor` (tabs and document operations), `Explorer` (tree and file operations),
`Commands` (registration, menus, palette, welcome) and the main file (layout, theme, status, closing). Menus are declared in XAML and bound
to commands by `Tag`; enabled state comes from the command registry. Features that belong to later phases are registered as commands with an
`UnavailableReason`, appear disabled and state their phase; they contain no placeholder logic.

## Not yet present

Editor, LanguageServices, Build, Debugging, Terminal, SourceControl, Templates, Extensions, Packages, Testing, Preview, and
integration tests. Each arrives as its own project when its phase starts.

## Editor (Phase 2)

```
MainWindow (tabs, commands, status bar)
   |  IEditorSurface            one surface displays every open document
   +-- MonacoEditorSurface      WebView2 + Assets/editor bundle   <-- normal path
   +-- PlainTextEditorSurface   TextBox fallback                  <-- WebView2 runtime or bundle missing, or the web process fails
                |
        EditorProtocol (Core)  <->  bridge.js (JavaScript)  <->  Monaco
```

- **One web view, many models.** Each document is a Monaco model; switching tabs swaps models and restores cursor/scroll state. The TabView is only the tab strip.
- **Text sync.** Monaco keeps the text. After a change it sends the full text debounced (200 ms, 1 s above 1 M characters) with an increasing revision;
  stale revisions are ignored. Before saving, closing or exiting, the app sends `flush` and waits (3 s) for the page to push anything still pending. If the page does
  not answer, the operation is cancelled rather than saving stale text. Documents use `\n` internally; the file's real line ending is applied at save time.
- **Shortcuts.** Keys pressed while the web view has focus never reach WinUI accelerators. The app sends the chords derived from `CommandDefinition.Shortcut`; the page
  swallows exactly those and reports them back, and the app runs the matching command. Editing keys (undo, find, ...) stay with Monaco.
- **Security boundary.** The page is served only from a virtual host mapped to the bundled folder; all other navigation, new windows and downloads are cancelled; host objects and
  dev tools (release) are off; permissions are denied except clipboard read for our origin; a content-security policy allows only `'self'` sources; every incoming message is
  validated by `EditorProtocol.TryParse` (type whitelist, GUID ids, integer ranges, chord pattern, size limit). Unvalidated messages are logged and dropped.
- **Bundle.** `Editor/web` is an npm project (pinned monaco-editor 0.57.0 and esbuild with a lockfile). `npm run build` writes `Assets/editor` (git-ignored): the host page and module,
  lazily loaded language chunks, and classic web workers for editor/JSON/CSS/HTML/TypeScript services. CI builds it before publishing and verifies the files exist.
- **WebView2 user data** lives in `%LOCALAPPDATA%\CarbonStudio\webview2`; the default location next to the executable would be read-only under Program Files.
