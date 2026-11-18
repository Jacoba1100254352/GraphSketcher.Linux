# Contributing

Thank you for helping bring GraphSketcher to Linux.

## Set up

1. Install the .NET 11 SDK pinned in `global.json`.
2. Clone this repository.
3. Run `dotnet restore GraphSketcher.Linux.sln`.
4. Run `dotnet test --solution GraphSketcher.Linux.sln --report-trx --coverlet`.
5. Run `dotnet run --project src/GraphSketcher.App`.

Please keep the core project UI-independent, add tests for document or data
behavior, and preserve source attribution when translating behavior from the
original Objective-C project.

## Pull requests

- Explain the user-facing behavior and compatibility impact.
- Include or update tests.
- Run the Release build and tests.
- Update `docs/COMPATIBILITY.md` when `.ograph` behavior changes.
- Do not describe the port as endorsed or official without written upstream
authorization.
