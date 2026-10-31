# Development guide

## Prerequisites

- Windows 10 1809 or later (Windows 11 recommended). Building does not need administrator rights.
- .NET 10 SDK (`global.json` allows any 10.0.1xx feature band).
- Visual Studio 2022 17.14+ or later with the *WinUI application development* workload, or the equivalent Windows App SDK build tooling.
- Node.js 22 (builds the Monaco editor bundle; the app still builds without it but falls back to the basic text editor).
- Git.
- To build the installer: the repository owner must have accepted the WiX v7 EULA (see below). WiX is restored from NuGet; nothing needs installing.

## Build, test, run

```powershell
dotnet restore
dotnet test tests/CarbonStudio.Core.Tests                         # Core logic; runs on any OS with .NET 10
cd src/CarbonStudio.App/Editor/web
npm ci; npm test                                                  # editor bridge tests (node's built-in runner)
npm run build                                                     # writes src/CarbonStudio.App/Assets/editor (git-ignored)
cd ../../../..
dotnet build src/CarbonStudio.App -r win-x64                      # WinUI app (Windows only)
dotnet run   --project src/CarbonStudio.App -r win-x64
```

The Core tests and the editor bundle also build and run on Linux/macOS. The app and installer only build on Windows.
Rebuild the bundle (`npm run build`) after changing anything under `Editor/web/src`.

### Formatting

CI runs `dotnet format whitespace . --folder --verify-no-changes --exclude node_modules`. Fix locally with the same command without `--verify-no-changes`.

## Publish and installer

```powershell
dotnet publish src/CarbonStudio.App -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
dotnet build installer/CarbonStudio.Installer/CarbonStudio.Installer.wixproj -c Release `
  -p:PublishDir="$PWD\artifacts\publish\win-x64\" -p:AcceptEula=wix7
```

Passing `-p:AcceptEula=wix7` means **you** accept the WiX v7 EULA (https://www.firegiant.com/wix/eula/). Output: `CarbonStudio-Setup-win-x64.msi` under the installer project's `bin` folder.
Binaries and the MSI are unsigned; SmartScreen will warn until a code-signing certificate is added (planned for a later phase).

## Continuous integration

`.github/workflows/build.yml`: `test-core` (format check, editor bridge tests, Core tests) then `build-app` (editor bundle, publish, file verification, artifact), then `installer`
(only when repository variable `WIX_EULA_ACCEPTED` = `wix7`), then `release` on `v*` tags. Actions are pinned to major versions; pin to commit SHAs before a public release.

## Where things are stored

| What | Where |
|---|---|
| User settings | `%LOCALAPPDATA%\CarbonStudio\settings.json` |
| Recent folders | `%LOCALAPPDATA%\CarbonStudio\recent-projects.json` |
| Embedded editor (WebView2) profile | `%LOCALAPPDATA%\CarbonStudio\webview2` |
| Logs (rolling, 5 MB / 10 files) | `%LOCALAPPDATA%\CarbonStudio\logs` (Help > Open Logs Folder) |
| Workspace settings | `<workspace>\.carbon\settings.json` |

## Dependency verification

Versions in `Directory.Packages.props` were chosen from published release information but **have not been restored** by the author. On the first restore, check:

1. `Microsoft.WindowsAppSDK 1.8.260710003` exists and supports `net10.0-windows10.0.19041.0`.
2. `Microsoft.Extensions.* 10.0.0`, `CommunityToolkit.Mvvm 8.4.0` (partial-property `[ObservableProperty]` support requires it), `xunit 2.9.3`, `xunit.runner.visualstudio 3.1.4`, `Microsoft.NET.Test.Sdk 17.14.1`.
3. `WixToolset.Sdk 7.0.0` (installer project only).

If the Windows App SDK build fails, see the fallback order in ADR-001.

## Updating the editor dependencies

`Editor/web/package.json` pins monaco-editor and esbuild exactly and `package-lock.json` records integrity hashes. To upgrade: change the version, run `npm install`, run `npm test` and `npm run build`,
commit both files, and do the manual smoke test in ADR-002. Prefer releases at least two weeks old.

## Troubleshooting

- **Output panel says "using the basic plain-text editor":** it names the reason. Missing bundle: run the npm steps above. Missing runtime: install the Microsoft Edge WebView2 Runtime. Timeout/crash: check the log for `Monaco`.
- **Editor area stays blank or "Starting editor…" for a long time:** the page did not report ready within 20 s; check the log, and whether antivirus blocks `msedgewebview2.exe`.

- **`NETSDK1100` / missing Windows targeting pack:** build on Windows; the app cannot be built on Linux or macOS.
- **XAML compiler errors mentioning `x:Bind` or `TreeView`:** the XAML and code-behind were never compiled before Phase 1 review; please report the exact message so it can be fixed.
- **App starts and immediately exits:** check `%LOCALAPPDATA%\CarbonStudio\logs` for a `Startup failed` or unhandled-exception entry.
- **Settings seem reset:** an invalid `settings.json` is renamed to `settings.json.corrupt-<timestamp>` next to it and a warning is written to the log.
- **Folder picker does nothing / throws:** pickers need the window handle; this is wired in `PickerService`. Report the exception text.
