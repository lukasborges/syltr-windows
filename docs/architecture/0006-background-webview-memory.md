# Background WebView memory policy

Date: 2026-07-16

## Decision

Keep the selected service WebView at
`CoreWebView2MemoryUsageTargetLevel.Normal` and request
`CoreWebView2MemoryUsageTargetLevel.Low` for every background service WebView.
The desired level is stored before initialization and reapplied whenever a
failed or disabled WebView is recreated.

Do not suspend background WebViews automatically. `TrySuspendAsync` pauses page
scripts and timers. That would delay web notifications, document-title unread
counts and other service state until the view is resumed, which conflicts with
Syltr's role as a real-time service aggregator.

## Evidence

The production configuration used for the comparison had eight enabled
services and one shared WebView2 environment. Both revisions were installed as
the same local MSIX, allowed to finish the parallel initial navigation, and
sampled three times at ten-second intervals after the process count and CPU had
settled.

| Metric | Previous revision | Low-memory background views | Difference |
| --- | ---: | ---: | ---: |
| Private working set, mean | 2,297.8 MB | 2,225.3 MB | -72.5 MB (-3.2%) |
| Private working set, median | 2,310.1 MB | 2,221.1 MB | -89.0 MB (-3.9%) |
| Total working set, mean | 3,385.3 MB | 3,292.0 MB | -93.3 MB (-2.8%) |
| Private commit, mean | 2,586.4 MB | 2,511.3 MB | -75.1 MB (-2.9%) |

The hosted sites perform periodic background work, so individual readings vary.
The controlled A/B result is evidence of a modest improvement, not a fixed
memory guarantee. WebView2 documents the low-memory target as best effort and
the operating system may swap some browser memory to disk.

## Consequences

- Scripts, connections, unread counters and notifications continue running in
  background services.
- Selecting a service restores its normal memory target before its content is
  presented, avoiding an avoidable foreground performance penalty.
- The shared browser and GPU processes remain shared across profiles; the
  renderer cost of each complex service still dominates total memory.
- A future opt-in aggressive mode may use sleeping or closed background views,
  but its UI must state that notifications and unread counts can be delayed.
- Startup peak memory is separate from steady-state memory because all enabled
  services currently navigate during startup. Staggered initialization can be
  evaluated independently to reduce contention without claiming a steady-state
  saving.
