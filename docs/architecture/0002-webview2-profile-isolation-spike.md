# WebView2 multiple-profile isolation spike

Date: 2026-07-15

## Decision

Continue with WinUI 3 and a shared `CoreWebView2Environment`. Each configured
service instance receives a stable named WebView2 profile derived from its
persisted service ID.

This decision began with the browser-storage gate. Authentication, OAuth/SSO
popups and the now-implemented permission, notification, download and process
recovery routes remain part of the continuous real-service compatibility
matrix.

## Test setup

- One explicit user data folder: `%LOCALAPPDATA%\Syltr\webview`.
- One shared WebView2 environment.
- Three simultaneous profiles: `profile-a`, `profile-b` and `profile-c`.
- The same local secure origin in all profiles: `https://syltr.test`.
- A native diagnostic writes a different token under the same `localStorage`
  key in every profile, reads all values back, then repeats after an app restart.
- The diagnostic can run unpackaged and self-contained through
  `scripts/run-isolation-spike.ps1`, so it does not require Windows Developer
  Mode.

## Observed evidence

The first run created three physical profile directories below the shared
environment:

```text
WV2Profile_profile-a
WV2Profile_profile-b
WV2Profile_profile-c
```

Each profile read back its own unique token. After a normal window close and
restart, each profile recovered the previous token and stored a second unique
token. No token from another profile appeared in that profile's LevelDB data.

A local `window.open()` test was then hosted in a second native WinUI window.
The popup displayed the value saved by its opener, confirming that the popup
WebView reused the originating profile while remaining behind the Engine API.

Finally, `profile-c` was deleted through the native confirmation flow. Its
definition was atomically removed from `services.json` and its physical profile
directory disappeared. After restart, only `profile-a` and `profile-b` were
created; both recovered their prior values and `profile-c` remained absent.

The Gmail and Microsoft Teams entry pages were then loaded in both remaining
profiles. UI Automation exposed separate Chromium accessibility profiles for
the two views (`Profile 2` and `Profile 3`), and both Google and Microsoft email
forms remained responsive. In a later MVP session, the user completed the real
Google Chat → Google → corporate SSO flow and confirmed that the redirects and
popup remained in Syltr. Broad two-account and Microsoft session-persistence
coverage is still tracked by the compatibility matrix.

The relaunched application remained responsive with eight WebView2 processes
sharing the same environment. No spike failure log was produced.

Permission requests are now intercepted inside `ServiceViewHost` and translated
to browser-independent events. The native window shows only the origin,
permission kind and isolated profile, then returns allow/deny plus whether the
choice should be saved in that profile. Paths, query strings and fragments are
discarded before the request reaches the window layer. The local diagnostic can
request camera, microphone, location, notifications and clipboard access;
physical-device and real-service validation remains open.

Downloads are now redirected to the Windows known Downloads folder. Suggested
names are treated as untrusted, stripped of path traversal and invalid Windows
names, length-limited and checked against both existing files and simultaneous
in-flight downloads before WebView2 receives the destination path. Progress,
completion and interruption are translated to Engine state events.

Fatal WebView2 failures now carry an explicit recovery action. Renderer exits
use reload first, while a browser-process exit replaces the WebView control and
reinitializes it with the same named profile. Non-fatal GPU, utility and frame
failures are left to WebView2's automatic recovery.

## Consequences

- The preferred shared-environment/multiple-profile architecture is viable for
  Chromium storage isolation and persistence.
- The app does not need one complete WebView2 user data folder per service.
- Profile deletion must target a named profile and must never delete the shared
  user data directory.
- The configured service list is the source of truth. A deleted profile must not
  be recreated unless the user explicitly adds that service instance again.
- Popup WebViews can reuse the opener profile under the shared environment; the
  validated Google/corporate SSO flow confirms the primary real-provider path.
- Real login tests remain mandatory because local storage isolation alone does
  not prove cookie, authentication broker or OAuth popup behavior.
- Permission decisions can be persisted by WebView2 within the originating
  profile. The native UI defaults to remembering the choice but lets the user
  opt out before allowing or denying it.
