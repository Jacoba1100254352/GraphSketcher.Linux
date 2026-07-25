# Windows release packaging

GitHub Actions creates self-contained, single-file portable packages for
`win-x64` and `win-arm64` on every `main` build and pull request. Pushing a
semantic-version tag such as `v0.1.0` also creates:

- `GraphSketcher-Windows-v0.1.0-win-x64.zip`
- `GraphSketcher-Windows-v0.1.0-win-arm64.zip`
- `GraphSketcher-Windows-v0.1.0-win-x64-Setup.exe`
- `SHA256SUMS`

The tag workflow verifies the Release build and tests before it packages
anything. The packaged x64 executable must also pass its Windows smoke test.
The ARM64 package is built on the same stable x64 Windows runner; native ARM64
execution is deferred until GitHub's Windows ARM runner leaves public preview.
The workflow then generates GitHub artifact attestations when the repository's
GitHub plan and token permissions support them.

## Installer behavior

`GraphSketcher.iss` builds an unsigned, per-user x64 Inno Setup installer. It:

- does not request administrator privileges;
- installs under `%LOCALAPPDATA%\Programs\GraphSketcher`;
- adds a current-user Start menu shortcut;
- offers an unchecked desktop-shortcut option; and
- registers `.graphsketch` for the current user.

The x64 installer also runs on Windows 11 ARM64 through Windows x64 emulation.
ARM64 users who prefer native binaries can use the portable `win-arm64`
package.

## Build the installer locally

Install the repository's pinned .NET SDK and Inno Setup 6.7.1, then run from
the repository root:

```powershell
dotnet restore src/GraphSketcher.App/GraphSketcher.App.csproj --runtime win-x64
dotnet publish src/GraphSketcher.App/GraphSketcher.App.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --no-restore `
  --output artifacts/GraphSketcher-Windows-win-x64

Copy-Item -LiteralPath LICENSE, NOTICE.md, THIRD-PARTY-NOTICES.md, README.md, ROADMAP.md `
  -Destination artifacts/GraphSketcher-Windows-win-x64
Copy-Item -LiteralPath docs `
  -Destination artifacts/GraphSketcher-Windows-win-x64 `
  -Recurse

& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" `
  "/DMyAppVersion=0.1.0" `
  "packaging\GraphSketcher.iss"
```

The installer is written to `artifacts/installer`. Release builds are not
code-signed until the project has a trusted Windows signing certificate, so
Windows SmartScreen may warn before launch.
