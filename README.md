# Syltr for Windows

Native Windows version of **Syltr**, an all-in-one desktop application for web-based messaging, email, calendar, task and AI services.

The application hosts services such as WhatsApp Web, Telegram, Slack, Discord, Teams, Gmail and others in a single native Windows window. Every configured service instance must have an isolated browser profile, allowing multiple accounts of the same service to remain logged in independently.

## Reference implementation and parity goal

The reference project is [lukasborges/syltr](https://github.com/lukasborges/syltr), the original native Linux/GNOME implementation.

This repository aims to be a nearly faithful Windows-native version of that project. It should preserve the same features, product behavior and overall project structure wherever practical, including the separation between the application shell, web engine, configuration, catalog and domain rules. Platform-specific code is adapted to Windows APIs, WinUI 3 and WebView2 so the application follows native Windows conventions without changing the core Syltr experience.

## Project status

The repository currently contains the initial, compilable scaffold:

- .NET 10;
- WinUI 3 and Windows App SDK 2.2;
- WebView2 through Windows App SDK;
- MSIX packaging support;
- separate projects for UI, domain, infrastructure and web hosting;
- xUnit test project;
- successful Debug build with zero warnings and zero errors;
- one initial placeholder test passing.

No product features have been ported yet. The next milestone is the domain model and catalog, followed by the WebView2 isolation spike described below.

## Product vision

Syltr for Windows should provide a Windows-native shell around web services while preserving the strengths of the original application:

- a compact service rail;
- multiple service instances and accounts;
- isolated persistent sessions;
- grouped instances of the same service;
- real favicons and unread badges;
- native Windows notifications;
- per-service mute and global do-not-disturb;
- downloads integrated with the Windows Downloads folder;
- safe handling of external links, OAuth and SSO popups;
- keyboard shortcuts and accessible native controls;
- persisted settings and service ordering;
- recovery from WebView process failures.

“Native Windows” means that the window, navigation, menus, dialogs, notifications, shortcuts, storage and operating-system integrations use Windows APIs and WinUI 3. The hosted services themselves remain web applications rendered by WebView2.

## Why this is a separate implementation

The original Syltr uses Rust, GTK4, libadwaita and WebKitGTK 6. Its architecture is clean, but most implementation code is coupled to GNOME or WebKitGTK APIs. Replacing only the graphical layer while keeping Rust would require significant Win32/COM/XAML integration and would reuse relatively little code.

The Windows version therefore uses C# and WinUI 3. Concepts, behavior, data formats, tests, assets and selected JavaScript can be migrated, but platform-specific code will be rewritten against WebView2 and Windows App SDK.

We intentionally do not introduce a Rust/.NET FFI boundary in the initial implementation. A shared native core should only be reconsidered if substantial cross-platform domain logic emerges later.

## Repository structure

```text
syltr-windows/
├── Syltr.slnx
├── src/
│   ├── Syltr.Core/              Domain models and platform-independent rules
│   ├── Syltr.Infrastructure/    JSON persistence and Windows integrations
│   ├── Syltr.Web/               WebView2 environment and ServiceViewHost
│   └── Syltr.Windows/           WinUI 3 application and MSIX package
└── tests/
    └── Syltr.Core.Tests/        Unit tests for domain behavior
```

### Dependency direction

```text
Syltr.Windows ───────► Syltr.Core
       │                    ▲
       ├────────────► Syltr.Infrastructure
       │                    │
       └────────────► Syltr.Web
                            │
                            └────────► Syltr.Core

Syltr.Core.Tests ───► Syltr.Core
```

`Syltr.Core` must not reference WinUI, WebView2 or Windows-only APIs. The UI should not manipulate `CoreWebView2` directly; all browser-specific behavior belongs behind an API in `Syltr.Web`.

## Planned architecture

### `Syltr.Core`

This project owns stable product concepts and rules:

- `ServiceDefinition`: id, name, URL, mute, disabled state and optional user-agent;
- `ServiceCatalogEntry` and catalog categories;
- service ID generation and slug normalization;
- URL normalization and validation;
- grouping multiple instances of the same service;
- unread-count parsing;
- application settings that do not depend on Windows;
- interfaces implemented by infrastructure where useful.

Domain behavior should be covered by unit tests before it is connected to WinUI.

### `Syltr.Infrastructure`

This project will implement:

- JSON service and settings stores;
- atomic writes and graceful handling of corrupted files;
- application data paths under Windows local application data;
- import of compatible configuration from the Linux version;
- Downloads folder resolution and collision-free file naming;
- native app notifications through Windows App SDK;
- logging and diagnostics abstractions.

The initial persistence schema should remain compatible with the original `services.json` when practical. Configuration can be imported, but WebKitGTK cookies and sessions cannot safely be converted into Chromium/WebView2 profiles; imported services will require login again.

### `Syltr.Web`

This project will expose a browser abstraction tentatively named `ServiceViewHost`:

```text
InitializeAsync
NavigateHome
Reload
GoBack
GoForward
SetMuted
SetUserAgent
UnreadCount
Favicon
Dispose
```

The exact API will evolve during the spike, but consumers must not depend on WebView2 event types.

Responsibilities include:

- creation and lifetime of the shared `CoreWebView2Environment`;
- one isolated WebView2 profile per service instance;
- navigation and process-failure recovery;
- permission policy;
- external link and popup handling;
- downloads;
- favicon discovery;
- unread detection;
- notification capture;
- user-agent configuration;
- script injection and the JavaScript/native message bridge;
- debug console forwarding when enabled.

### `Syltr.Windows`

This project owns the native user experience:

- main window and title bar;
- service rail and grouped instance chooser;
- content host for service views;
- add/edit/remove dialogs;
- context menus and drag-and-drop ordering;
- keyboard shortcuts;
- settings and about UI;
- empty, loading, error and disabled states;
- activation from a Windows notification;
- application lifecycle and MSIX manifest.

## Browser profile strategy

The preferred design is:

1. Create one shared `CoreWebView2Environment` in a writable folder under local application data.
2. Create one stable WebView2 profile for every service instance.
3. Use the persisted service ID as the profile name, for example `whatsapp`, `whatsapp-2` or `teams-work`.
4. Reuse the same environment and profile for OAuth/SSO popup WebViews opened by that service.
5. Explicitly close WebView controls and release browser resources when an instance is removed.

Multiple profiles under one user data folder should isolate cookies, storage, cache, permissions and preferences while allowing WebView2 to share runtime processes. This assumption must be proven by the spike before the rest of the UI is built.

## Migration map from the original project

| Original area | Windows strategy |
| --- | --- |
| Service model and JSON schema | Port to C# and keep compatible where practical |
| Static catalog and categories | Port data, preferably to an embedded JSON resource |
| URL normalization | Port with equivalent tests |
| Service ID generation | Port with equivalent tests |
| Unread parsing from page title | Port with equivalent tests |
| Per-service user-agent rules | Port, then retest against Chromium |
| Service grouping and ordering | Port domain behavior; rebuild UI in WinUI |
| GTK/libadwaita window and dialogs | Rewrite in XAML/WinUI 3 |
| WebKitGTK `ServiceView` | Rewrite as WebView2 `ServiceViewHost` |
| Per-session WebKit data directories | Replace with WebView2 multiple profiles |
| GIO notifications | Replace with `AppNotificationManager` |
| XDG config/data paths | Replace with Windows local application data |
| XDG Downloads lookup | Replace with Windows known-folder APIs |
| Favicon JavaScript | Adapt bridge to `window.chrome.webview.postMessage` |
| Web notification shim | Prefer native WebView2 APIs; keep a tested service-worker fallback if needed |
| WebKit media workarounds | Do not port unless Chromium testing demonstrates a need |
| MPRIS/PipeWire workarounds | Remove; Linux-specific |
| Hunspell system discovery | Do not port initially; evaluate WebView2/Chromium spell checking |
| SVG service assets and translations | Reuse after license and rendering review |

Linux/WebKit workarounds must never be copied blindly. Every compatibility script should have a documented failing service, a test procedure and a removal condition.

## Delivery plan

### Phase 0 — Foundation

Status: **in progress**

- [x] Create an independent Git repository.
- [x] Create the WinUI 3/MSIX application.
- [x] Create Core, Infrastructure, Web and test projects.
- [x] Establish project references and dependency direction.
- [x] Validate Debug build and test execution.
- [ ] Replace template namespaces, titles and placeholder classes.
- [ ] Add repository documentation, formatting rules and CI.
- [ ] Add the initial application icon and licensing files.

### Phase 1 — Domain and persistence

- [ ] Port `ServiceDefinition` and settings models.
- [ ] Port ID generation, URL normalization and unread parsing.
- [ ] Port the service catalog.
- [ ] Add JSON serialization compatibility tests.
- [ ] Implement atomic service/settings persistence.
- [ ] Define local application data paths.
- [ ] Add an explicit schema/version migration strategy.

Exit criterion: catalog and service configuration can be created, saved, loaded and tested without WinUI or WebView2.

### Phase 2 — WebView2 isolation spike

This is the most important technical gate.

- [ ] Create a shared WebView2 environment with an explicit user data folder.
- [ ] Create at least three simultaneous profiles.
- [ ] Prove two independent logins to the same service.
- [ ] Prove that closing and reopening the app preserves both sessions.
- [ ] Prove that removing one profile does not affect another.
- [ ] Validate Google and Microsoft authentication.
- [ ] Validate OAuth/SSO popup handling within the originating profile.
- [ ] Validate normal and service-worker notifications.
- [ ] Validate upload, download and clipboard image paste.
- [ ] Validate audio, video, microphone and camera permissions.
- [ ] Measure memory with 5, 10 and 20 loaded services.
- [ ] Validate crash/process-failure recovery.

Exit criterion: profile isolation, authentication and the critical WebView integrations work reliably enough to support the product.

If WinUI 3 hosting exposes a blocking limitation, `Syltr.Core` and `Syltr.Infrastructure` should remain unchanged while the host is evaluated in WPF + WebView2. This is a fallback decision, not the default plan.

### Phase 3 — Minimum viable product

- [ ] Implement the native window and compact service rail.
- [ ] Add services from the catalog or a custom URL.
- [ ] Add, edit, remove, disable and reorder services.
- [ ] Group multiple instances of the same service.
- [ ] Host and switch between persistent service views.
- [ ] Implement back, forward, home and reload.
- [ ] Open user-clicked external links in the default browser.
- [ ] Keep OAuth/SSO navigation in the appropriate in-app popup.
- [ ] Display favicon and unread badge.
- [ ] Add loading, empty, disabled and error states.
- [ ] Implement the original keyboard shortcuts where they fit Windows conventions.

Exit criterion: a user can configure multiple services/accounts, restart the app and use them reliably.

### Phase 4 — Feature parity

- [ ] Native notifications with per-service mute.
- [ ] Global do-not-disturb.
- [ ] Notification activation that selects the correct service.
- [ ] Downloads to the Windows Downloads folder.
- [ ] Collision-free download filenames and completion notification.
- [ ] Custom user-agent per service.
- [ ] Spell-check behavior and language strategy.
- [ ] Debug console capture and diagnostics.
- [ ] Configuration import from the Linux application.
- [ ] Portuguese and English resources.
- [ ] Accessibility review and full keyboard navigation.

### Phase 5 — Hardening and distribution

- [ ] Automated test matrix for core services.
- [ ] CI build and tests for pull requests.
- [ ] Release build and MSIX generation.
- [ ] Package identity, publisher and signing strategy.
- [ ] WebView2 Evergreen prerequisite handling.
- [ ] x64 release first; ARM64 after validation.
- [ ] Upgrade and uninstall behavior.
- [ ] Privacy, telemetry and diagnostic policy.
- [ ] License and third-party notices.
- [ ] Beta release and feedback loop.

## Compatibility test matrix

Every supported service should be tested for the following behaviors where applicable:

- initial load and login;
- session persistence after restart;
- multiple isolated accounts;
- OAuth/SSO popup flow;
- sending and receiving messages;
- unread badge updates;
- native notifications, including service-worker notifications;
- notification click behavior;
- file upload and download;
- clipboard text and image paste;
- audio playback and recording;
- video playback;
- microphone and camera permissions;
- external links;
- custom protocols and deep links;
- context menu and spell checking;
- behavior after a WebView process failure.

The initial high-priority matrix is:

1. WhatsApp Web;
2. Microsoft Teams;
3. Slack;
4. Discord;
5. Telegram Web;
6. Gmail/Google authentication;
7. Outlook/Microsoft authentication;
8. one custom URL service.

Website compatibility is a continuous product responsibility because hosted services can change independently of Syltr releases.

## Resource and lifecycle policy

The original application loads every enabled service so that unread counts and notifications continue in the background. The Windows version should initially preserve that behavior for parity, then measure its cost.

Possible later optimization:

- keep all services loaded by default;
- expose an optional “sleep inactive services” mode;
- clearly communicate that sleeping may delay unread counts and notifications;
- use measured memory thresholds rather than arbitrary eviction;
- never destroy browser data when merely suspending a view.

## Security and privacy principles

- Treat all hosted web content as untrusted.
- Keep service profiles isolated.
- Do not expose arbitrary native APIs to JavaScript.
- Validate every message received through the WebView bridge.
- Restrict privileged actions to known message shapes and expected origins.
- Avoid logging message contents, tokens, cookies or authentication URLs.
- Open external navigation with explicit, testable policy.
- Request camera, microphone and notification permissions intentionally.
- Prefer the Evergreen WebView2 runtime for security updates.
- Do not add telemetry without an explicit product decision and documentation.

## Data locations and migration

The final paths will be centralized in `Syltr.Infrastructure`. The intended categories are:

```text
Local application data
├── config/
│   ├── services.json
│   └── settings.json
├── webview/                 Shared WebView2 user data folder
├── logs/                    Diagnostic logs, if enabled
└── migrations/              Optional migration state
```

Requirements:

- never store mutable data beside the executable;
- keep browser data separate from human-readable configuration;
- use atomic replacement for JSON writes;
- tolerate missing optional fields;
- back up configuration before a destructive schema migration;
- deleting a service profile must require an intentional product action.

## Development

### Requirements

- Windows 10 version 1809 or later; Windows 11 is recommended for development;
- .NET 10 SDK;
- Developer Mode enabled for local packaged-app execution;
- WebView2 Evergreen Runtime;
- Visual Studio with WinUI tooling is optional but recommended for XAML work.

### Build

```powershell
dotnet restore Syltr.slnx
dotnet build Syltr.slnx -c Debug
```

### Test

```powershell
dotnet test Syltr.slnx -c Debug
```

### Run

```powershell
dotnet run --project src/Syltr.Windows/Syltr.Windows.csproj
```

The application is configured for packaged execution with debug identity support. Distribution packaging and signing are not finalized.

## Engineering conventions

- Nullable reference types remain enabled.
- New domain behavior requires unit tests.
- Platform-independent code belongs in `Syltr.Core`.
- WebView2 types must not leak outside `Syltr.Web` unless an explicit architecture decision documents why.
- Windows integration belongs in `Syltr.Infrastructure`, not code-behind.
- Code-behind should coordinate view concerns; product rules belong in testable classes.
- Avoid service-specific hacks in generic hosting code. Put them in named recipes with rationale.
- Keep compatibility scripts as separate embedded resources instead of large C# string literals.
- Record meaningful architecture decisions in the repository when a spike resolves an open question.

## Current open decisions

The following items are deliberately unresolved until evidence is collected:

- exact `ServiceViewHost` public API;
- whether native WebView2 notifications fully cover persistent service-worker notifications;
- popup UX for OAuth/SSO;
- whether all enabled services remain loaded indefinitely;
- spell-check language controls;
- configuration storage path for packaged versus unpackaged development;
- installer/update channel and code-signing identity;
- minimum supported Windows release for the first public build;
- whether ARM64 ships with the first stable release;
- final application ID and package publisher.

## Estimated roadmap

For one experienced developer working full-time, the initial estimate is:

- foundation and technical spike: 1–2 weeks;
- domain, persistence and MVP UI: 2–3 weeks;
- feature parity and service compatibility: 2–4 weeks;
- packaging, hardening and beta feedback: 1–2 weeks.

Total: approximately **6–10 weeks** for a credible first public version. Website-specific issues and signing/store requirements can change this estimate.

## Licensing

The original Syltr project is licensed under GPL-3.0-or-later. Reusing its code or GPL-covered assets means this implementation should remain GPL-3.0-or-later unless ownership and licensing of every reused component allow a different decision.

Service names and logos may be trademarks of their respective owners. Before distribution, every bundled asset must have its source and license reviewed, and the application must clearly state that it is not affiliated with the hosted services.

## Immediate next steps

1. Remove template placeholder classes and normalize namespaces/title to `Syltr`.
2. Implement `ServiceDefinition`, settings and the static catalog in `Syltr.Core`.
3. Port URL normalization, service ID generation and unread parsing with tests.
4. Implement JSON persistence in `Syltr.Infrastructure`.
5. Build the WebView2 multiple-profile spike in `Syltr.Web`.
6. Record spike results and update the open decisions in this README before building the full UI.
