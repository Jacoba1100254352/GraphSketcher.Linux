# Contributing

Thank you for helping bring GraphSketcher to Windows.

## Set up

1. Install the .NET 10 SDK.
2. Clone this repository.
3. Run `dotnet restore`.
4. Run `dotnet test`.
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
