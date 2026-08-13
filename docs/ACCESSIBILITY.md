# Accessibility and scaling contract

Status: first keyboard and UI Automation contract implemented locally; DPI,
high-contrast, assistive-technology, and owner acceptance remain open.

## Implemented contract

- Every interactive WPF control in the current 11 XAML files has either an
  explicit `AutomationProperties.Name` or a fixed visible button label.
- The command palette cycles Tab and Control+Tab within its modal surface,
  Escape closes it, and the previous focus target is restored.
- Empty command results and Remote Assist session changes use polite UI
  Automation live settings.
- Workspace transitions run only when Windows reports client-area animation as
  enabled; disabling that Windows preference removes the transition.
- Shared control styles retain visible keyboard-focus states.

`eng/verify-accessibility-contracts.ps1` parses the shipping XAML and fails when
an interactive control loses its programmatic name, the command focus cycle, the
empty-result announcement, or the reduced-motion policy. It currently covers 86
interactive controls. The canonical verifier and Windows workflow both run it.

## Current evidence

- Accessibility contract: 86/86 interactive controls across 11 XAML files.
- Release build: 0 warnings and 0 errors.
- App/control/lifecycle: 34/34.
- Fresh native 1280x820 Command Palette, Activity, and Remote Assist captures:
  no clipping, visual regression, or state ambiguity observed.

This is source/runtime contract evidence, not screen-reader certification.

## Remaining acceptance work

- run Windows UI Automation client inspection over the live focus order, names,
  control types, enabled state, and live regions;
- complete keyboard-only traversal for every workspace, modal, confirmation,
  cancellation, and focus-restoration path;
- capture and inspect 100%, 125%, 150%, and 200% Windows scaling across
  representative 1366x768, 1440p, and 4K displays;
- exercise Windows high contrast and confirm status is never color-only;
- run Narrator and at least one independent screen-reader smoke pass;
- retain busy, empty, failure, recovery, destructive confirmation, and
  cancellation pixels;
- obtain the owner's explicit `KEEP`, `REVISE`, or `REJECT` visual decision.

No WCAG, screen-reader, high-contrast, scaling, or accessibility-conformance
claim is made until those runtime gates are completed.
