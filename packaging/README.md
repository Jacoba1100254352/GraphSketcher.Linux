# Linux release packaging

GitHub Actions builds self-contained packages for `linux-x64` and
`linux-arm64` on every main-branch build and pull request. Semantic-version
tags such as `v0.1.0-preview.1` publish:

- `GraphSketcher-Linux-v0.1.0-preview.1-linux-x64.tar.gz`
- `GraphSketcher-Linux-v0.1.0-preview.1-linux-arm64.tar.gz`
- `GraphSketcher-Linux-v0.1.0-preview.1-amd64.deb`
- `GraphSketcher-Linux-v0.1.0-preview.1-arm64.deb`
- `GraphSketcher-Linux-v0.1.0-preview.1-x86_64.AppImage`
- `SHA256SUMS`

The x64 executable is launched under Xvfb before packages are accepted. The
ARM64 package is built on an x64 runner and is not emulated during CI.

## Desktop integration

The Debian and AppImage layouts include:

- `io.github.jacoba1100254352.GraphSketcher.desktop`;
- a 512-pixel application icon;
- AppStream metadata;
- MIME registration for `.graphsketch` and `.ograph`; and
- a command named `graphsketcher`.

The Debian package installs under `/usr/lib/graphsketcher` and uses a small
launcher in `/usr/bin`. Installation and removal refresh desktop and MIME
caches when the corresponding tools are available.

## Build packages locally

Install the pinned .NET SDK plus `dpkg-deb`. AppImage creation is optional and
requires an AppImageKit `appimagetool` executable.

```bash
dotnet restore src/GraphSketcher.App/GraphSketcher.App.csproj -r linux-x64
dotnet publish src/GraphSketcher.App \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  --no-restore \
  -o artifacts/publish-linux-x64

packaging/build-linux-packages.sh \
  linux-x64 \
  0.1.0-preview.1 \
  artifacts/publish-linux-x64 \
  artifacts/packages
```

Set `APPIMAGETOOL_PATH` to include an AppImage in the output. The packaging
script rejects unknown runtimes, missing executables, and unsafe version
strings before writing artifacts.
