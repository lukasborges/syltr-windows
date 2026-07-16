# Accessibility validation plan

The shell has automated source checks for its accessible names, semantic
headings, live status region and keyboard-only actions. The following runtime
pass remains required on Windows before the accessibility roadmap item is
complete.

## Keyboard-only

1. Traverse the header, service rail, hosted page and do-not-disturb control
   with `Tab` and `Shift+Tab`; focus must remain visible.
2. Select services with the arrow keys and open the selected service actions
   with `Shift+F10`.
3. Exercise the documented service shortcuts, including `Ctrl+1…9`,
   `Ctrl+Tab`, `Ctrl+Shift+Tab`, `Alt+Left/Right`, `Ctrl+R` and `Ctrl+N`.
4. Open the service catalog. Focus must start in search, every visible result
   must be reachable, and `Esc` must close the window and restore the owner.
5. Complete and cancel add, edit, remove, permission and import dialogs without
   a pointer.

## Narrator

1. Confirm every icon-only header control announces its localized action.
2. Confirm the window and service-state headings are exposed as headings.
3. Confirm each rail item announces the service/instance name and aggregate
   unread count.
4. Confirm service status changes and errors are announced without moving
   focus.
5. Confirm catalog category headings, search, custom-service action and each
   add-service row have distinct names.

## Visual accessibility

1. Check all states in Windows light, dark and high-contrast themes.
2. Check 100%, 150%, 200% and 400% text/display scaling at the minimum supported
   window size.
3. Confirm focus indicators, active service, unread badge, disabled state and
   errors are not communicated by color alone.
