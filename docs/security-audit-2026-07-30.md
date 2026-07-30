# Security and workflow audit — 2026-07-30

## Scope and architecture

GraphSketcher.Linux is a local desktop application. It has no network listener,
remote API, account database, authentication flow, authorization roles,
promotion or demotion operation, administrator console, background service, or
server-side persistence. Those web-application surfaces are therefore not
applicable.

The repository has one GitHub administrator and no pending collaborator
invitations. GitHub Actions default to read-only repository permissions; only
the release job receives narrowly scoped write, identity-token, attestation,
and artifact-metadata permissions.

The applicable trust boundaries are:

- native `.graphsketch` JSON documents;
- legacy plain or ZIP-wrapped `.ograph` XML documents;
- pasted CSV and TSV text;
- SVG and CSV exports;
- NuGet dependency resolution; and
- CI packaging and release publication.

## Existing controls verified

- Native documents are limited to 64 MiB and 64 JSON levels.
- Typed allocation is preflighted at 256 series, 250,000 total points, and
  10,000 annotations.
- Model validation rejects non-finite values, invalid dimensions and axis
  ranges, unsupported enum values, duplicate identifiers, and oversized text.
- Legacy XML prohibits DTDs and external resolvers.
- ZIP input limits compressed input, entry count, aggregate declared expansion,
  and `contents.xml` expansion, and accepts exactly one root `contents.xml`.
- Local native-document saves use a sibling temporary file and atomic replace.
- SVG values are emitted through `XmlWriter`.
- Release downloads of `appimagetool` use HTTPS, an exact version, and an exact
  SHA-256 digest.
- Repository secret scanning and push protection are enabled. Dependabot and
  secret-scanning alert APIs reported no open alerts.

## Findings remediated

1. Delimited input had no resource boundary. It now limits input to 16,777,216
   characters, 250,001 parsed rows, 512 columns, 16,384 characters per field,
   10,000 reported invalid cells or rows, 256 output series, and 250,000 output
   points.
2. Imported series could be appended beyond the current document's aggregate
   series or point limits. Both totals are now checked before the document is
   modified.
3. User-controlled series names and labels could begin with spreadsheet
   formulas in CSV exports. Formula-like cells are now neutralized before RFC
   4180 escaping.
4. JSON text containing XML-prohibited control characters could fail SVG
   export with a low-level writer exception. Export now rejects it as a handled
   invalid-data error.
5. CI and release actions used movable major-version tags. Every action now
   uses an immutable commit digest with its human-readable major version
   retained as a comment. Checkout credentials are not persisted.
6. NuGet transitive resolution was not locked. Per-project lockfiles are now
   committed, and CI and release restores use locked mode.
7. Self-contained publish properties caused the local host runtime to enter
   Linux release resolution. Those properties now activate only with an
   explicit runtime, leaving Linux x64 and ARM64 as the portable locked targets.
8. Release attestations were allowed to fail without blocking publication.
   Both package and checksum-manifest attestations are now required.
9. Direct dependencies were behind current compatible releases. Avalonia,
   Microsoft.NET.Test.Sdk, xUnit, its Visual Studio runner, and coverlet were
   updated.

## Validation

- Exact .NET SDK 10.0.302 archive verified against Microsoft's published
  SHA-512 metadata.
- Locked solution restore: passed.
- Release build with warnings treated as errors: passed.
- Core tests: 97 passed, 0 failed.
- `dotnet format --verify-no-changes`: passed.
- Self-contained cross-publish for `linux-x64` and `linux-arm64`: passed.
- NuGet vulnerable, deprecated, and top-level outdated checks: no findings.
- Trivy NuGet/runtime and workflow-configuration scan: no findings.
- Gitleaks full-history scan: no findings.
- Shell, XML, and YAML syntax checks: passed.
- GitHub Dependabot alerts: none.
- GitHub secret-scanning alerts: none.
- GitHub code scanning is not configured; local compiler, tests, Trivy, and
  secret scanning remain the enforced source gates.

The macOS audit host cannot execute Linux GUI packages. The public release
workflow remains responsible for its Ubuntu build, x64 Xvfb smoke test,
package metadata checks, ARM64 cross-package, checksums, attestations, and
release publication.
