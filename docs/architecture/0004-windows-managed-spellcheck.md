# Windows-managed spell checking

## Context

The Linux application discovers Hunspell dictionaries and lets users select
spell-check languages per application. The Windows shell initially persisted the
same selection, but did not apply it to WebView2.

WebView2 automatically provides Chromium spell checking from Windows language
preferences. Its supported `CoreWebView2Profile` API exposes several profile-wide
settings, but no spell-check dictionary list. `CoreWebView2EnvironmentOptions.Language`
controls browser UI and the HTTP `Accept-Language` value; it is not a replacement
for selecting multiple spell-check dictionaries. Edge browser spell-check policies
are also not listed among the policies supported by the WebView2 Runtime.

The Chromium `Preferences` file currently contains a private
`spellcheck.dictionaries` value. Writing that file would depend on an undocumented
schema, race the browser process and risk losing unrelated profile settings.

## Decision

Syltr uses WebView2's Windows-managed spell checking. The main menu reports the
preferred Windows languages and opens `ms-settings:regionlanguage` so users can
manage installed languages and dictionaries. The Linux-compatible
`spell_languages` configuration field remains readable and writable for schema
compatibility, but the Windows application does not claim to apply it.

## Consequences

- Spell checking follows Windows and WebView2 updates without private profile
  mutation.
- Users change dictionaries in Windows Settings and restart Syltr so new WebView2
  processes see the updated preferences.
- The Windows UI does not reproduce Linux's per-language checkboxes.
- If WebView2 adds a supported dictionary API, the `Spellcheck` module is the
  place to adopt it and restore direct controls.
