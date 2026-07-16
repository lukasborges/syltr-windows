# Windows app notification bridge

Date: 2026-07-15

## Decision

Handle web notifications in `ServiceViewHost`, translate them to a
browser-independent model, and publish them through Windows App SDK
`AppNotificationManager`. Keep an in-window `InfoBar` as a fallback when system
notification registration or delivery is unavailable.

## Rationale

WebView2 requires the host to explicitly report when a handled web notification
was shown, clicked or closed. The Engine retains those callbacks without
exposing WebView2 types. The Window maps a Windows notification ID to the
originating web notification and profile. Clicking either the Windows
notification or the in-window action selects that profile and reports the click
back to the web application.

`AppNotificationManager` is the current Windows App SDK API for both packaged
and unpackaged desktop apps. Unpackaged registration is performed at runtime;
the MSIX manifest also declares toast activation and its COM server for packaged
execution.

## Consequences

- Notification content remains web-controlled and must be treated as untrusted.
- Only the profile ID and an opaque notification ID are placed in activation
  arguments; URLs, cookies and message metadata are not included.
- Activation can arrive on a background thread or before service tabs finish
  loading, so profile selection is dispatched and queued until the UI is ready.
- Normal and service-worker notification delivery and click callbacks still
  require end-to-end runtime validation before the Phase 2 checkbox is closed.
