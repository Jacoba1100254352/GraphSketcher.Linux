# GraphSketcher for Windows

[![Windows CI](https://github.com/Jacoba1100254352/GraphSketcher.Windows/actions/workflows/ci.yml/badge.svg)](https://github.com/Jacoba1100254352/GraphSketcher.Windows/actions/workflows/ci.yml)

<img src="assets/graphsketcher-logo.svg" width="112" alt="GraphSketcher for Windows logo">

An independent, open-source Windows port of
[GraphSketcher](https://github.com/graphsketcher/GraphSketcher): a fast,
direct-manipulation app for sketching graphs and plotting tabular data.

> **Project status:** Early preview (`0.1.0-preview.1`). The app is useful today for
> plotting and editing data, but this is not yet full feature parity with the
> original Mac/iPad application. This community port is not currently endorsed
> by the original maintainers.

![GraphSketcher showing a cooling-experiment graph with error bars, a reference curve, labels, and the series inspector](docs/images/graphsketcher-preview.png)

## What works

- Native Windows desktop UI with light and dark theme support
- Point, straight-line, smooth-line, and area plots
- Direct point creation, selection, dragging, and deletion
- CSV/TSV text and pasted spreadsheet data import
- Multiple data series with editable names, colors, markers, and line styles
- Linear best-fit lines with slope, intercept, and R²
- Linear and logarithmic axes, grid/tick controls, and automatic scaling
- Labels, error bars, legend, axis titles, and graph title
- Undo and redo
- Native `.graphsketch` JSON documents with validated, atomic saves
- Import of plain-XML and zipped legacy `.ograph` files
- Export to SVG and CSV
- Self-contained Windows x64 and arm64 builds

See [the compatibility matrix](docs/COMPATIBILITY.md) for exact legacy
`.ograph` coverage and known gaps.

## Download

Published versions are available on the
[Releases page](https://github.com/Jacoba1100254352/GraphSketcher.Windows/releases).
Choose the `win-x64` download for most Windows 10/11 computers or `win-arm64`
for Windows on ARM. Each package is self-contained; installing .NET separately
is not required.

Until the first signed release is published, Windows may display a
SmartScreen warning because the executable is not code-signed.

## Quick start

1. Open the app and choose **Import data**.
2. Paste rows copied from Excel or CSV/TSV text into the import dialog.
3. Use the right inspector to adjust series and axes.
4. Use **Point**, **Draw**, or **Text** to add elements directly.
5. Save an editable `.graphsketch` file or export an SVG for a report.

The repository also includes a
[getting-started graph](samples/Getting%20Started.graphsketch).

## Build from source

Requirements:

- .NET 10 SDK
- Windows 10 version 1809 or later, Windows 11, macOS, or Linux for development

```powershell
dotnet restore
dotnet test -c Release
dotnet run --project src/GraphSketcher.App
```

Create a self-contained Windows package:

```powershell
dotnet publish src/GraphSketcher.App -c Release -r win-x64 --self-contained true
```

## Architecture

- `GraphSketcher.Core` — graph model, math, data import, serializers, legacy
  `.ograph` compatibility, and vector export
- `GraphSketcher.App` — Avalonia desktop UI and interactive renderer
- `GraphSketcher.Core.Tests` — regression and compatibility tests

The port uses modern C# and Avalonia rather than trying to compile the legacy
Cocoa/UIKit application on Windows. This keeps the core portable and lets CI
produce Windows builds without relying on the original Xcode 5-era OmniGroup
framework stack.

## Roadmap

The first preview focuses on the graphing workflow. Important parity work still
includes richer freehand/fill editing, PDF/PNG export, advanced typography,
equation curves, complete `.ograph` round-tripping, accessibility review, and
signed installers. See [ROADMAP.md](ROADMAP.md).

## Attribution and license

Graph Sketcher was created by Robin Stewart in 2007 and was further developed
by The Omni Group. The original source was released in 2014 under the
MIT-style Omni Source License 2007.

See [NOTICE.md](NOTICE.md) for complete attribution and the independent-port
disclaimer, and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for bundled
runtime and library notices. This repository is distributed under the terms
in [LICENSE](LICENSE).
