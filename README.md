# Syltr for Windows

An all-in-one desktop application for web-based messaging, email, calendar,
task and AI services, native to **Windows**. It brings services such as WhatsApp
Web, Telegram, Slack, Discord, Teams and Gmail into a single window, each with
its own isolated session for cookies and storage.

This is the Windows counterpart of the original
[Syltr for Linux](https://github.com/lukasborges/syltr), adapted to Windows APIs
and native interface conventions.

**Stack:** C# · .NET 10 · WinUI 3 · Windows App SDK · WebView2

## Development

### Requirements

- Windows 10 version 1809 or later;
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0);
- [WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/);
- Windows Developer Mode for packaged execution;
- Visual Studio with WinUI tooling is optional, but recommended for XAML work.

Restore dependencies and build the solution:

```powershell
dotnet restore Syltr.slnx
dotnet build Syltr.slnx -c Debug
```

Run the application:

```powershell
dotnet run --project src/Syltr/Syltr.csproj
```

For an unpackaged build that does not require Developer Mode, use:

```powershell
.\scripts\run-isolation-spike.ps1
```

### Tests

```powershell
dotnet test Syltr.slnx -c Debug
```

Tests live in `tests/Syltr.Tests` and follow the same module organization as
the application. New domain behavior and bug fixes should include unit tests.

### Diagnostics

Set `SYLTR_DEBUG=1` before starting the application to record WebView2 console
messages in the local diagnostic log:

```powershell
$env:SYLTR_DEBUG = "1"
dotnet run --project src/Syltr/Syltr.csproj
```

Build and distribution instructions are documented in
[`docs/distribution.md`](docs/distribution.md). Contribution conventions are in
[`CONTRIBUTING.md`](CONTRIBUTING.md).

## Structure

| Path | Responsibility |
| --- | --- |
| `src/Syltr/App.xaml(.cs)` | Application startup, lifecycle and shared resources |
| `src/Syltr/Window/` | Native window, service rail, actions and dialogs |
| `src/Syltr/Engine/` | WebView2 sessions, navigation, downloads, notifications and browser integration |
| `src/Syltr/Config/` | Service models, settings, persistence and application data paths |
| `src/Syltr/Catalog/` | Catalog of known services |
| `src/Syltr/Icon/` | Service tiles, favicons and unread badges |
| `src/Syltr/Spellcheck/` | Windows spell-check integration |
| `src/Syltr/Localization/` and `src/Syltr/Strings/` | Localization infrastructure and translated resources |
| `tests/Syltr.Tests/` | Unit and architecture tests |
| `scripts/` | Local build, packaging and asset-generation helpers |
| `docs/` | Architecture decisions and development documentation |

The application accesses WebView2 only through the public API exposed by
`Syltr.Engine`; UI code should not manipulate browser-specific types directly.
Platform-independent rules belong in `Catalog` or `Config`, while native
presentation and interaction belong in `Window`.

## License

GPL-3.0-or-later
