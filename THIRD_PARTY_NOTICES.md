# Third-party notices

Licenses below were stated from general knowledge of each project and must be re-checked against the exact
package versions before a public release (each NuGet package carries its own license file).

| Component | Used for | License (to verify per version) |
|---|---|---|
| Microsoft.WindowsAppSDK 1.8.x | WinUI 3 runtime and tooling | Microsoft Software License Terms |
| CommunityToolkit.Mvvm | MVVM source generators | MIT |
| Microsoft.Extensions.DependencyInjection / Logging (10.0.x) | DI and logging abstractions | MIT |
| xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk | Test framework (tests only) | Apache-2.0 / MIT |
| WiX Toolset v7 | MSI installer build (build-time only) | Open Source Maintenance Fee EULA; the repository owner must review and accept it |

| monaco-editor 0.57.0 | Code editor, bundled into the app (`Assets\editor`, license text shipped as `LICENSE-monaco-editor.txt`) | MIT |
| marked 14.0.0, dompurify 3.4.15 | Dependencies of monaco-editor (bundled) | MIT; MPL-2.0 OR Apache-2.0 |
| esbuild 0.28.2 | Builds the editor bundle (build-time only, not shipped) | MIT |

Planned, not yet included: xterm.js (MIT) in a later phase.
