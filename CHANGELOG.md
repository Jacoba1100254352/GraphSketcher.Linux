# Changelog

All notable changes are documented here. The project follows Semantic
Versioning while in preview; breaking changes may occur before 1.0 and will be
called out explicitly.

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
