# Linux fidelity audit

This audit compares the Windows shell directly with the reference implementation
in `lukasborges/syltr`. Platform behavior remains native to Windows, but product
structure, control placement and service workflows should stay recognizable.

## Shell and service workflows

| Reference behavior | Windows status |
| --- | --- |
| One GNOME-style header with icon-only controls | Implemented with a custom WinUI title bar and flat icon buttons |
| Main menu: spell-check, about, quit | Implemented; spell-check reports Windows preferences and opens system language settings |
| DND at the right edge with active/inactive notification glyphs | Implemented |
| 84 px icon-only rail | Implemented |
| 40 px neutral tile, 11 px radius, favicon clamped to 16–24 px | Implemented from `src/icon.rs` constants |
| Full-row active highlight with a left accent stroke | Implemented |
| Grouped-instance affordance, chooser and aggregate unread badge | Implemented |
| Right-click menu: reload, home, edit, mute, disable, remove | Implemented in the same order with state-aware labels |
| Drag whole service groups to reorder | Implemented and persisted |
| Clicked `target=_blank` links open externally while OAuth/SSO stays in-app | Implemented with click classification before WebView2 popup handling |
| Searchable, category-grouped add catalog with 28 px icons | Implemented in an owned native Windows dialog; all 37 reference recipes are present |
| Separate custom-service action | Implemented as an icon action beside the catalog search field |
| First run opens the add dialog instead of creating sample services | Implemented |
| Empty and disabled service states | Implemented |
| Linux shortcut set (`Ctrl+1…9`, `Ctrl+PgUp/PgDown`, `Alt+↑/↓`, `F5`, `Ctrl+Q`) | Implemented, with additional Windows conventions retained |

## Intentional Windows adaptations

- Window caption controls, Mica/backdrop behavior, dialogs and notification UI
  use WinUI and Windows App SDK rather than imitating GTK widgets pixel-for-pixel.
- Browser sessions use WebView2 profiles. WebKitGTK session data cannot be reused.
- Permission prompts identify the requesting origin and isolated service profile
  using a native Windows dialog.
- WebView2 manages spell-check dictionaries from Windows language preferences;
  its public profile API does not support the Linux per-language toggles.

## Remaining fidelity work

- Validate service-specific user-agent quirks against Chromium. Linux's Safari and
  Chrome-on-Linux spoofing must not be copied without a failing Windows service.
- Port a website-specific compatibility script only when WebView2 reproduces the
  corresponding failure; the Linux Teams workaround is WebKit-specific.
- Validate the automatic external-link versus OAuth/SSO classification against
  real services, including the reported Google-to-corporate-SSO flow.
