# Privacy policy

Syltr is a local desktop client for web services. The Syltr project does not
operate an account service, analytics backend or telemetry endpoint, and the
application does not send usage data to the Syltr developer.

Each configured service runs in an isolated WebView2 profile and communicates
directly with the service selected by the user. Those third-party services may
collect data under their own privacy policies. Syltr does not control their web
content or data practices.

Configuration, browser profiles and diagnostic files are stored locally on the
user's Windows account. Web console capture is disabled by default and runs
only when Syltr is started with `SYLTR_DEBUG=1`. Diagnostic logs are never
uploaded automatically and should be reviewed before they are shared because
hosted pages control their console messages.

Native notifications contain information supplied by the configured web
service. Downloads and permission decisions are handled locally through
Windows and WebView2. Removing Syltr does not delete accounts or data held by
third-party services.

Privacy questions and project feedback can be submitted through the project's
GitHub issue tracker: <https://github.com/lukasborges/syltr-windows/issues>.
