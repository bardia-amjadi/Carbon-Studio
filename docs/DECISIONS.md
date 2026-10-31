# Decision records

Status key: **Decided** (acted on), **Provisional** (chosen, evidence incomplete, with a verification gate).

## ADR-001: Platform and pinned versions: Provisional

WinUI 3 on the Windows App SDK **1.8.x** (pinned to `1.8.260710003`, listed as a supported release on Microsoft's downloads page),
`net10.0-windows10.0.19041.0`, minimum OS Windows 10 1809 (build 17763). Newer Windows App SDK 2.x releases exist; 1.8 was chosen
as the conservative, widely documented line, with an upgrade to 2.x as a separate, deliberate change.

**Unverified:** exact NuGet availability of every pinned version and .NET 10 compatibility of this Windows App SDK build could not be
checked from the authoring environment (NuGet was unreachable). The first CI restore/publish is the verification gate. If it fails, the
fallback order is: adjust the pin in `Directory.Packages.props`; try Windows App SDK 2.x; target `net8.0-windows` for the app only.

## ADR-002: Code editor = Monaco (ESM, bundled with esbuild) in WebView2: Decided (implemented in Phase 2, unverified in a real WebView2)

Options reviewed: AvalonEdit (WPF only, cannot be hosted directly in WinUI 3: rejected); a native WinUI editor control built on Win2D from
the community (small, single-maintainer, no LSP-oriented features); Monaco Editor in a WebView2 control.

**Choice: Monaco in WebView2**, hosted by our own thin wrapper rather than a third-party wrapper package. Monaco is the editor core of VS Code (MIT) and already provides
multi-cursor, find/replace, bracket matching, folding and syntax highlighting; its provider APIs map well onto LSP later. To my understanding PowerToys' Registry Preview
uses a WebView2-hosted Monaco editor for a comparable purpose (not independently confirmed).

**Build format (Phase 2 finding): ESM, not AMD.** monaco-editor 0.57.0 still ships a `min/` AMD build, but its own changelog says the AMD build is deprecated, unsupported and
changed significantly, and its bundled `loader.js` is an old copy. The supported route is the ESM build, which needs a bundler. We bundle with esbuild (pinned, with a lockfile for integrity) into
`Assets/editor`: one module entry with code splitting (each language grammar is a lazy chunk) and classic web workers for the editor, JSON, CSS, HTML and TypeScript services.
Dependencies are 2+ weeks old releases (monaco-editor 0.57.0 from 2026-09-24, esbuild 0.28.2 from 2026-08-08).

**Offline and isolation:** everything is served from a virtual host mapped to the bundled folder; no remote origin is reachable (content-security policy `'self'` only, no `unsafe-eval`;
the bundle was scanned and the editor host and workers it uses contain no `eval`/`new Function`, apart from one occurrence inside the TypeScript worker, which runs under no page policy).

**Fallback:** if the WebView2 runtime is not installed, the bundle is missing, startup fails or times out, or the web process later crashes, the app switches to a basic `TextBox` editor and tells the user
(Output panel and, for crashes, a dialog). Features the fallback cannot do are disabled, not faked.

Trade-offs: WebView2 runtime dependency (preinstalled on Windows 11; Windows 10 may need the Evergreen runtime; the installer does not bootstrap it yet); higher memory use than a native control; a
JavaScript/C# bridge that must stay narrow and validated; ~15 MB of editor assets; LSP is bridged by us later, Monaco has no LSP client.

**Unverified risks (cannot be checked without Windows):**
1. Whether the in-tree XAML command-palette overlay draws above the WebView2 control (I believe WinUI 3 hosts it with composition, so it should; if not, the palette must move into a `Popup`).
2. Monaco under the strict content-security policy in real WebView2 (workers, styles, fonts, clipboard).
3. Key forwarding: chords are intercepted in the page; edge cases with IME or non-US keyboard layouts are untested.
4. Performance with a 10 MB file (full-text sync after a 1 s debounce).

**Gate:** the first CI run proves only that the bundle builds and ships. A manual smoke test on Windows is required: open/edit/save a .cs, .json and a 5 MB file; find/replace; go to line; theme switch;
Ctrl+S/Ctrl+Shift+P while the editor has focus; palette visible over the editor; kill the WebView2 process and confirm the fallback.

## ADR-003: Terminal = xterm.js in WebView2 + our own ConPTY host: Provisional

Windows Terminal's own control is not a supported, redistributable WinUI 3 component. Community projects that embed terminals in WinUI were found
(ConPTY-based hosts rendering with xterm.js, and wrapper libraries around the Windows Terminal control), but wrappers are small projects with prerelease-style
versioning. The plan is to render with xterm.js (MIT) in the same WebView2 infrastructure as the editor and to own a small ConPTY wrapper
(`CreatePseudoConsole`, a documented Win32 API, Windows 10 1809+, matching our minimum OS). This avoids a WPF dependency and reuses ADR-002's hosting work.

**Gate:** Phase 4 spike: PowerShell session with colours, resize, Ctrl+C, and clean child-process teardown. Not prototyped in Phase 1.

## ADR-004: Unpackaged, self-contained app installed by an MSI: Decided (build unverified)

`WindowsPackageType=None` with `WindowsAppSDKSelfContained=true`. This avoids MSIX packaging and signing requirements and lets a conventional WiX MSI install
plain files. Costs: larger install, no MSIX auto-update or package identity (revisit if an API needs it).

The installer is per-machine (Program Files, one UAC prompt at install). Per-user installation was considered but WiX's per-user component rules (HKCU key paths for
every component) add complexity that is unwarranted in Phase 1. Uninstall never touches `%LOCALAPPDATA%\CarbonStudio` or any user project.

## ADR-005: WiX v7 with explicit EULA acceptance: Decided

WiX v7 is distributed under the Open Source Maintenance Fee EULA. Accepting a license is the repository owner's call, so CI builds the installer only when the manual-run checkbox is ticked or the repository variable
`WIX_EULA_ACCEPTED` equals `wix7`; otherwise it emits a visible warning and a job summary instead of failing or silently skipping.
Authoring uses the WiX v4+ schema; `Files` harvesting and `BindPath` syntax in `Package.wxs` are unverified until the first installer build.

## ADR-006: Settings are layered JSON with quarantine on corruption: Decided

See ARCHITECTURE.md. A bad file never prevents startup and is never deleted; the user sees what happened.

## ADR-007: Test strategy: Decided

Logic lives in `Core` and is tested with xUnit. UI is verified manually. Because the authoring sandbox could not reach NuGet, the Core tests were executed against the real
test source files through a throwaway xUnit-compatible runner; CI runs them under real xUnit and is the authoritative result.

## ADR-008: Editor/app protocol and text synchronisation: Decided

JSON messages with a `type`, defined in `EditorProtocol.cs` and `bridge.js` (parity of action names is a unit test). The web page is treated as untrusted input: messages are validated field by field, sized,
and unknown types are dropped. Monaco owns the live text; the app holds a debounced copy plus a `flush` handshake before any operation that reads it (save, close, exit, encoding change). A failed handshake
cancels the operation. Alternatives considered: sending incremental edits (more complex, error-prone to replay in C#) and sending every keystroke's full text (too costly for large files). Line endings are not stored in
Monaco models (always LF); the document's own line-ending and encoding settings are applied only when writing the file, so switching them never touches the editor text.
