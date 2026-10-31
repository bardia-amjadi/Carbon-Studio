# Carbon Studio

A local-first development environment for Windows, built with C#, .NET 10 and WinUI 3.

> **Status: Phase 2 of 7 (editor) written.** The Core library and the editor bridge are implemented and unit-tested.
> The WinUI 3 shell, the Monaco editor integration, the CI pipeline and the installer project are written but **have not
> been built or run yet**: that happens for the first time in GitHub Actions. See [Feature status](#feature-status)
> for exactly what is and is not verified.

## Feature status

| Area | Status |
|---|---|
| Core library: settings (layered), recent projects, workspace + project detection, file system service, text encoding/EOL handling, document manager, command registry + palette search, rolling file logging, DI | **Implemented, unit-tested** |
| Phase 2 Core: language catalog, editor message protocol (strict validation), shortcut map, encoding and line-ending control, editor settings | **Implemented, unit-tested** (217 Core tests) |
| Editor bridge (JavaScript) between the app and Monaco: documents, debounced text sync, flush, themes, options, shortcuts | **Implemented, 16 node tests passing**; bundle builds and reproduces from the lockfile |
| WinUI 3 shell: title bar + menus, activity bar, explorer, tabs, welcome screen, command palette, status bar, themes, settings, persisted layout | **Written, not compiled** (needs a Windows build) |
| Monaco editor in WebView2 (syntax highlighting for ~40 languages, multi-cursor, find/replace, go to line, bracket matching, auto-indent, format document for JSON/CSS/HTML/JS/TS, tab/space and encoding/EOL settings) with plain-text fallback | **Written, never run** (no WebView2 available where it was authored) |
| GitHub Actions: format check, Core + JS tests, editor bundle, app publish, installer, tag release | **Written, never run** |
| WiX installer | **Skeleton, never built** (needs WiX EULA acceptance, see below) |
| Language servers / IntelliSense for C#, Python, C++, project templates + wizard, build/run, terminal, Git, workspace search, debugger, tests, packages, preview, VS Code extensions, remote workspaces | **Not implemented.** Planned for Phases 3 to 7; commands exist but are disabled and name their phase. |

Monaco's own built-in language features (completion and diagnostics for JSON, CSS, HTML, JavaScript and TypeScript) are active
because they ship inside Monaco. They are **not** a Language Server Protocol integration, and there is no code intelligence for C#, Python or C++ yet.

If the WebView2 runtime or the editor bundle is missing, Carbon Studio says so in the Output panel and falls back to a basic text box editor
(see [ADR-002](docs/DECISIONS.md)).

## Roadmap

1. Foundation (done, awaiting first CI confirmation)
2. **Editor: Monaco in WebView2** (this phase, awaiting first CI confirmation)
3. Project templates, template engine, creation wizard
4. Build/run tasks, output and problems, integrated terminal
5. Git, workspace search/replace, C# language server
6. Debugger, Test Explorer, packages, web preview
7. Extension host, remote-workspace providers

## Repository layout

```
CarbonStudio.sln
src/CarbonStudio.Core      Cross-platform (net10.0) domain + services. No UI dependencies.
src/CarbonStudio.App       WinUI 3 shell (net10.0-windows10.0.19041.0), unpackaged, self-contained.
src/CarbonStudio.App/Editor/web   Monaco host (JavaScript bridge, esbuild bundle script, node tests, pinned lockfile).
tests/CarbonStudio.Core.Tests   xUnit tests for Core.
installer/CarbonStudio.Installer   WiX v7 MSI project (skeleton).
assets/                    Logo (SVG/PNG) and application icon.
docs/                      Architecture, decision records, development guide.
.github/workflows/build.yml   CI.
```

Projects from the long-term plan (Editor, LanguageServices, Build, Terminal, SourceControl, Templates, ...) are
created in the phase that gives them code, not as empty shells.

## Quick start

Requirements: Windows 10 1809+ (Windows 11 recommended), .NET 10 SDK, Node.js 22 (for the editor bundle), and Visual Studio with the
*WinUI application development* workload (or the Windows App SDK build tooling). No administrator rights are needed to build.

```powershell
dotnet test tests/CarbonStudio.Core.Tests                       # Core tests
cd src/CarbonStudio.App/Editor/web; npm ci; npm test; npm run build; cd ../../../..   # editor bridge tests + bundle
dotnet run --project src/CarbonStudio.App -r win-x64            # run the app
dotnet publish src/CarbonStudio.App -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

Details, installer instructions and troubleshooting: [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## CI artifacts

| Artifact | Contents |
|---|---|
| `CarbonStudio-TestResults` | TRX results of the Core test run |
| `CarbonStudio-win-x64` | Published application folder (`CarbonStudio.exe`, dependencies and the `Assets\editor` Monaco bundle) |
| `CarbonStudio-Setup-win-x64` | `CarbonStudio-Setup-win-x64.msi`; only produced after the WiX EULA is accepted: tick the checkbox under *Actions > build > Run workflow*, or set repository variable `WIX_EULA_ACCEPTED` = `wix7` |

Version tags (`v*`) additionally create a GitHub release with a portable zip and, when built, the MSI.

## Verification

What has actually been run, and where:

- **Core tests:** 217 tests, all passing, on Linux with .NET 10.0.114. NuGet was unreachable in that environment, so they ran through a throwaway
  xUnit-compatible runner against the real test sources (not committed). They have **not** yet run under real xUnit. One test also checks that the C# editor
  action names match the JavaScript bridge, so the two sides cannot drift apart silently.
- **Editor bridge:** 16 node tests pass against a fake Monaco. `npm ci`, `npm test` and `npm run build` were run on a clean copy and reproduce
  the bundle (117 files, 15.4 MB, monaco-editor 0.57.0) from the committed lockfile.
- **Not verified, because no browser or Windows was available:** the bundle has never been loaded in a real browser or WebView2, so Monaco's actual
  behaviour (rendering, workers under the content-security policy, clipboard, key handling) is unproven.
- **Formatting gate:** `dotnet format whitespace --verify-no-changes` passes locally and was confirmed to fail on a deliberate violation.
- **WinUI app:** all C# files parse without syntax errors with the SDK's Roslyn and all XAML is well-formed XML. Nothing has been compiled against the Windows App SDK.
- **Installer, CI workflow, pinned NuGet versions:** not yet exercised. The first GitHub Actions run is the real test.

## Known limitations (Phase 1)

- Single workspace at a time; the last folder is not reopened automatically.
- Find/replace works inside the open file only; workspace-wide search is Phase 5.
- The editor needs the Microsoft Edge WebView2 Runtime (included with Windows 11; on Windows 10 it may need installing). Without it the basic text editor is used.
- Language features beyond Monaco's built-ins (C#, Python, C++ intelligence) are not implemented.
- Format Document only does something for languages whose Monaco formatter exists (JSON, CSS, HTML, JavaScript, TypeScript).
- Explorer does not watch the file system; use Refresh after external changes.
- No settings sync, no telemetry (none exists), no account.
- Installer is per-machine (needs elevation), has no desktop-shortcut option and no UI dialogs yet.
- Unsigned binaries and installer: Windows SmartScreen will warn on first run until code signing is added.

## Third-party software

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). A project license has not been chosen yet; that decision belongs to the repository owner.
