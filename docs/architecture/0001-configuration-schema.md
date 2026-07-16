# Configuration schema and migrations

Syltr keeps `config/services.json` as a top-level array and `config/settings.json`
as a top-level object so configurations remain compatible with the Linux
implementation. Schema metadata therefore lives separately in
`migrations/schema.json`.

Version 1 is the initial Windows schema. An existing configuration without a
schema marker is treated as version 1, so importing compatible Linux files does
not require rewriting them.

Future migrations must follow these rules:

1. Migrations are sequential and explicitly registered from one version to the
   next. The app must refuse unknown future versions.
2. A migration validates its input before making changes.
3. Before a destructive migration, `services.json`, `settings.json` and the
   schema marker are copied to a timestamped folder below `migrations/backups/`.
4. Each changed file is written through a temporary file in the same directory
   and atomically replaced.
5. The schema marker is updated last. A failure therefore leaves either the old
   version active or enough backup data for recovery.
6. WebView2 profile data is never modified by a configuration-schema migration.
   Removing browser data remains a separate, intentional product action.
