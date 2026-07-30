# Changelog

All notable changes are documented here. The project follows Semantic
Versioning while in preview; breaking changes may occur before 1.0 and will be
called out explicitly.

## 0.1.0-preview.2 — 2026-07-30

Security and dependency maintenance preview.

### Security

- Bound pasted CSV/TSV input by length, rows, columns, field length, reported
  issues, series, and total points before modifying the open document.
- Prevent imported data from appending beyond aggregate document limits.
- Neutralize spreadsheet formulas in user-controlled CSV text cells.
- Reject text that cannot be represented safely in exported SVG.
- Pin GitHub Actions to immutable commits, disable persisted checkout
  credentials, and require release attestations to succeed.
- Commit NuGet lockfiles and enforce locked restores for Linux CI and releases.

### Changed

- Updated Avalonia to 12.1.1, Microsoft.NET.Test.Sdk to 18.8.1, xUnit to
  3.2.2, xunit.runner.visualstudio to 3.1.5, and coverlet.collector to 10.0.1.
- Updated actions/checkout to v7 and actions/setup-dotnet to v6.
- Apply self-contained runtime settings only during explicit Linux publishes,
  keeping ordinary builds and lockfiles platform-independent.

## 0.1.0-preview.1 — 2026-07-25

First public Linux-port preview.

### Added

- Avalonia/.NET 10 Linux desktop application
- Direct point drawing, connected series, annotations, selection, and dragging
- CSV, TSV, and spreadsheet-paste import
- Series, axis, canvas, legend, and logarithmic-scale inspectors
- Automatic scaling, nice ticks, linear regression, error bars, and area fills
- Undo/redo and validated `.graphsketch` JSON documents
- Safe import of common zipped and plain-XML `.ograph` documents
- Bounded native-document loading with malformed-input protection
- SVG and CSV export
- Self-contained x64/ARM64 tarballs and Debian packages, plus an x64 AppImage
- XDG desktop entry, application metadata, icon, and `.graphsketch` MIME registration
- Packaged-app smoke testing, checksums, and bundled dependency notices
- Tests for core math, import, serialization, history, SVG, and legacy files

### Known limitations

- This is an independent community port, not an upstream-endorsed release.
- Legacy `.ograph` import is intentionally lossy for rich text, fills, groups,
  locking, snapping, and several advanced styles.
- Package signing, PNG/PDF export, freehand tools, and full accessibility testing
  remain on the roadmap.
