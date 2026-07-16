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
- a single `Syltr` application project organized by the same responsibilities as the Linux project;
- an xUnit test project with an architecture test enforcing the module layout;
- successful Debug build with zero warnings and zero errors;
- normalized `Syltr` assembly, namespaces and application titles.

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
│   └── Syltr/
│       ├── App.xaml             Application startup and resources
│       ├── Catalog/             Known service recipes
│       ├── Config/              Service models, settings and persistence
│       ├── Engine/              WebView2 sessions and browser integrations
│       │   ├── Unread/          Unread-count detection and parsing
│       │   ├── UserAgent/       Per-service user-agent rules
│       │   └── WebAppScripts/   Service-specific compatibility scripts
│       ├── Icon/                Service icons, favicons and unread badges
│       ├── Spellcheck/          Spell-check integration
│       ├── Window/              Native window, rail, actions and dialogs
│       ├── Assets/              Windows application and package assets
│       └── Syltr.csproj         WinUI 3 application and MSIX package
└── tests/
    └── Syltr.Tests/             Unit tests for ported behavior
```

### Structure parity with Linux

The source tree deliberately mirrors the modules in [lukasborges/syltr](https://github.com/lukasborges/syltr):

| Linux reference | Windows equivalent | Responsibility |
| --- | --- | --- |
| `src/main.rs` | `src/Syltr/App.xaml(.cs)` | Startup, lifecycle and application resources |
| `src/window/` | `src/Syltr/Window/` | Window, rail, actions, dialogs and native controls |
| `src/engine/` | `src/Syltr/Engine/` | WebView2 sessions, scripts, favicons, unread state and downloads |
| `src/config/` | `src/Syltr/Config/` | Service definitions, settings, persistence and application data paths |
| `src/catalog.rs` | `src/Syltr/Catalog/` | Catalog of known services |
| `src/icon.rs` | `src/Syltr/Icon/` | Service tile, favicon and unread badge behavior |
| `src/spellcheck.rs` | `src/Syltr/Spellcheck/` | Platform spell-check integration |

This mapping is an architecture contract for the delivery plan. New functionality should first be located in the equivalent reference module, then implemented in its Windows counterpart. A structural divergence is allowed only when required by .NET, WinUI 3, WebView2 or MSIX, and the reason must be documented here. `Assets` and the package manifests remain inside the WinUI project because Windows packaging tooling expects them there.

Module boundaries are enforced through namespaces and public abstractions. `Window` must not manipulate `CoreWebView2` directly; browser-specific behavior belongs behind the public API exposed by `Engine`.

## Planned architecture

### `Catalog` and `Config`

These modules own stable product concepts and rules:

- `ServiceDefinition`: id, name, URL, mute, disabled state and optional user-agent;
- `ServiceCatalogEntry` and catalog categories;
- service ID generation and slug normalization;
- URL normalization and validation;
- grouping multiple instances of the same service;
- unread-count parsing;
- application settings that do not depend on Windows;
- interfaces implemented by infrastructure where useful.

Domain behavior should be covered by unit tests before it is connected to WinUI.

`Config` also owns persistence and platform storage services:

- JSON service and settings stores;
- atomic writes and graceful handling of corrupted files;
- application data paths under Windows local application data;
- import of compatible configuration from the Linux version;
- Downloads folder resolution and collision-free file naming;
- native app notifications through Windows App SDK;
- logging and diagnostics abstractions.

The initial persistence schema should remain compatible with the original `services.json` when practical. Configuration can be imported, but WebKitGTK cookies and sessions cannot safely be converted into Chromium/WebView2 profiles; imported services will require login again.

### `Engine`

This module will expose a browser abstraction tentatively named `ServiceViewHost`:

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

### `Window`

This module owns the native user experience:

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

### `Icon` and `Spellcheck`

`Icon` owns the reusable visual behavior for service tiles, favicons, grouping and unread badges. `Spellcheck` owns Windows-specific spell-check discovery and configuration. These responsibilities remain separate to preserve the same boundaries as the Linux reference project.

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

The implementation order follows the module map above. Each porting task starts from the behavior and tests in the matching Linux module, preserves its public responsibilities and then adapts only the platform-specific implementation. Phase reviews must verify both feature parity and structural parity before moving forward.

### Phase 0 — Foundation

Status: **in progress**

- [x] Create an independent Git repository.
- [x] Create the WinUI 3/MSIX application.
- [x] Create the WinUI application and xUnit test projects.
- [x] Align source modules with the Linux reference structure.
- [x] Validate Debug build and test execution.
- [x] Replace template namespaces, titles and placeholder classes.
- [ ] Add repository documentation, formatting rules and CI.
- [ ] Add the initial application icon and licensing files.

### Phase 1 — Domain and persistence

- [ ] Port `ServiceDefinition` and settings models into `Config`.
- [ ] Port ID generation and URL normalization into `Config`, and unread parsing into `Engine`.
- [ ] Port the service catalog into `Catalog`.
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

If WinUI 3 hosting exposes a blocking limitation, the platform-independent behavior in `Config` and `Catalog` should remain unchanged while the host is evaluated in WPF + WebView2. This is a fallback decision, not the default plan.

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

The final paths will be centralized in `Syltr.Config`. The intended categories are:

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
dotnet run --project src/Syltr/Syltr.csproj
```

The application is configured for packaged execution with debug identity support. Distribution packaging and signing are not finalized.

## Engineering conventions

- Nullable reference types remain enabled.
- New domain behavior requires unit tests.
- Ported code belongs in the Windows module that corresponds to its Linux reference module.
- WebView2 types must not leak outside `Syltr.Engine` unless an explicit architecture decision documents why.
- Persistence and application data paths belong in `Syltr.Config`, not code-behind.
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

1. Port `ServiceDefinition`, settings, service ID generation and URL normalization into `Config`, with tests.
2. Port the static service catalog into `Catalog`, preserving the reference recipes and categories.
3. Port unread parsing and its tests into `Engine/Unread`.
4. Implement JSON persistence and Windows application data paths in `Config`.
5. Build the WebView2 multiple-profile spike in `Engine` behind `ServiceViewHost`.
6. Port the `Window`, `Icon` and `Spellcheck` behavior module by module, recording any intentional divergence from Linux in this README.
