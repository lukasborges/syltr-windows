# Contributing to Syltr for Windows

## Development setup

Use Windows 10 1809 or newer, the .NET 10 SDK and the Evergreen WebView2
Runtime. Visual Studio with WinUI tooling is optional.

```powershell
dotnet restore Syltr.slnx
dotnet test tests/Syltr.Tests/Syltr.Tests.csproj
powershell -ExecutionPolicy Bypass -File scripts/run-isolation-spike.ps1
```

## Architecture

- Domain and persistence rules belong in `Catalog` or `Config` and require unit
  tests.
- WebView2 types stay inside `Engine`.
- Native interaction and presentation belong in `Window`.
- Service tile, favicon and badge behavior belongs in `Icon`.
- Website-specific workarounds require a named failing service, reproduction
  steps and a removal condition.

Do not commit browser profiles, cookies, tokens, local configuration, build
outputs or crash dumps. Use diagnostic accounts for compatibility testing.

## Pull requests

Keep changes focused, run the full tests, confirm the unpackaged x64 build and
describe any service-specific manual validation. Record architectural decisions
in `docs/architecture` when a spike resolves an open platform question.
