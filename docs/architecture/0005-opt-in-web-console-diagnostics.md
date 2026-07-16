# Opt-in web console diagnostics

## Decision

Syltr captures WebView2 console messages only when the process starts with
`SYLTR_DEBUG=1`. The Engine subscribes to the official
`Runtime.consoleAPICalled` Chrome DevTools Protocol event and publishes a typed
message without exposing WebView2 objects to the Window layer.

The Window writes the messages to
`%LOCALAPPDATA%\Syltr\logs\web-console.jsonl`. Each entry contains the UTC time,
profile name, level, message, source origin and optional line/column. Source
paths, query strings and fragments are discarded. Messages are limited to 4
KiB, and the log rotates to one backup after reaching 2 MiB.

## Privacy and failure behavior

Console text is controlled by hosted websites and can contain account or
application data. Capture is therefore disabled by default, local only, and
never sent as telemetry. The diagnostics menu can open the local log folder.
Writing or enabling capture is best-effort and must never prevent a service from
loading.
