# Architecture

## Design goals

The original GraphSketcher combined a reusable model with separate AppKit and
UIKit shells, but the shared layer still depended heavily on Apple and
OmniGroup frameworks. The Linux port preserves the platform-neutral core from
the modern Avalonia Windows port and supplies a Linux-native application,
packaging, and release surface.

```text
GraphSketcher.App
  Avalonia windows, tools, inspectors, file lifecycle
          │
          ▼
GraphSketcher.Core
  Model ── Math/statistics ── Import/export ── History
```

`GraphSketcher.Core` has no UI or platform dependencies. It can be tested
headlessly and reused by a future command-line converter or another frontend.

## Document model

`GraphDocument` is the aggregate root. It owns canvas settings, X/Y axes,
series, and annotations. Every public load/save boundary validates:

- finite coordinates and dimensions;
- ordered axis bounds;
- positive logarithmic values and bases;
- unique stable IDs;
- bounded names, labels, descriptions, and colors;
- recognized enum values.

The native `.graphsketch` format serializes this model as readable,
versioned JSON. Writes use a sibling temporary file followed by an atomic move
when a local path is available. Loads are bounded to 64 MiB, 64 levels of JSON
depth, 256 series, 250,000 total points, and 10,000 annotations.

## Rendering

The interactive canvas draws the validated model with Avalonia's
resolution-independent drawing API. Coordinate transforms are kept in data
space until rendering. Linear and logarithmic mappings share the same screen
layout; invalid logarithmic points are omitted.

SVG export is implemented in the core and does not scrape the screen. This
keeps exports deterministic and independent of display DPI or theme.

## Editing and history

Tools mutate the document model, then record a serialized snapshot through the
bounded `HistoryManager`. Snapshots are independent of UI object identity and
therefore also exercise the serializer during normal editing.

## Legacy input boundary

`.ograph` is treated as untrusted input. The importer:

- accepts plain XML or ZIP containers with `contents.xml`;
- prohibits DTDs and external entity resolution;
- limits entry counts and expanded sizes before parsing;
- rejects non-finite coordinates and invalid axis ranges;
- resolves original ID references in a second pass;
- reports unsupported constructs rather than pretending full parity.

The application opens legacy files without overwriting them and saves edits to
the native cross-platform format.

## Delimited-data and export boundaries

Pasted CSV and TSV text is bounded before parsing and again while materializing
series and points. A rejected import is checked against the current document's
aggregate limits before any series are appended.

CSV export writes invariant numeric values and prefixes formula-like
user-controlled text so spreadsheet applications display it as text. SVG
export uses an XML writer for escaping and rejects characters that XML 1.0
cannot represent.

## Release model

Every push to `main` and every pull request builds and tests on Ubuntu. The
packaged x64 application is launched under Xvfb as a runtime smoke test. Tags
matching `v*` produce self-contained x64 and ARM64 tarballs and Debian
packages, an x64 AppImage, checksums, GitHub artifact attestations, and a
GitHub Release.
