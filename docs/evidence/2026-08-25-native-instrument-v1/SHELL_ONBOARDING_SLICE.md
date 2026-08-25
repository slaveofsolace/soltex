# Native shell and first-run slice

Parent source: `3e02d9e9ade1776c5a4e45c49bbd3cd3f68be689`

Branch: `sol/soltex-native-instrument-v1`

Evidence date: 2026-08-25

## Implemented

- Seven-step first-run setup for local data, appearance, Audio, Whisper,
  Capture, shortcuts, and Advanced Lab safety.
- Versioned progress is written after each reviewed decision. Closing setup does
  not produce a completion receipt; the next launch resumes at the first
  unreviewed decision.
- Appearance controls execute live through the existing `ThemeProfile` boundary.
- Unfinished Capture and Advanced Lab capability is labelled unavailable while
  remaining reviewable; review state does not change capability state.
- Settings can reopen setup, and `Ctrl+K` routes setup searches to that working
  entry point.
- Expanded, compact, and narrow shell layouts reclaim workspace width while
  retaining accessible navigation names and Security status through the owning
  workspace and compact tooltip.

## Verification

- Release solution build: 0 warnings, 0 errors.
  - Log: `artifacts/native-instrument-v1/shell-onboarding/release-build-2026-08-25T1710Z.log`
  - SHA-256: `899d885e88727c3720a51871ff7839fa6b69cee93709daf13c2e6f69feec32a7`
- Application suite: 53/53 passed in 1665.9 ms.
  - Log: `artifacts/native-instrument-v1/shell-onboarding/app-tests-2026-08-25T1711Z.log`
  - SHA-256: `7133610a030e4af61b9f7b9ee5b6d126f642cf6e4320acd7f9cacb2f39e048cb`
- Standard dark appearance setup render: 1280x820.
  - Artifact: `artifacts/native-instrument-v1/shell-onboarding/onboarding-appearance-final.png`
  - SHA-256: `8395881ce3abde3eaf69e201a608988e669f1f8727a260a4c178953cd08afec6`
- Compact dark privacy setup render: 1100x720.
  - Artifact: `artifacts/native-instrument-v1/shell-onboarding/onboarding-privacy-compact-final.png`
  - SHA-256: `e490d56be04316089564eaf225c453f645b717b45e927654e3c30ba9b83e093c`
- `git diff --check`: passed before final staging.
- The canonical PowerShell design-token and identity launchers produced no
  output and were stopped by their exact task-owned sessions. A repository-
  external, shell-free implementation of the same checked-in policies passed
  after staging: 13 XAML files and 324 tracked text files with 8 reasoned
  allowlist entries.
  - Log: `artifacts/native-instrument-v1/shell-onboarding/policy-gates-final2-2026-08-25T1219Z.log`
  - SHA-256: `6a316946c9c041dbd9265d2e55b9690840e863e3bc92505e4c8c0fb1b827ae8f`
- Human Cortex ledger schema 2.0.0: passed.
  - Log: `artifacts/native-instrument-v1/shell-onboarding/cortex-ledger-final2-2026-08-25T1219Z.log`
  - SHA-256: `6f509bfb6565fcba695ac340445244ef459fb83fcf737632076795591baed0d5`

All build, test, and render processes were launched by the repository-external
PID-owned runner with bounded timeouts, log paths, and PID receipts. Roslyn
compiler PID 48200 and its child were proven task-owned from their command line
and creation time, then stopped after they retained the completed test log.

## Visual inspection

The setup surface has one dominant decision, restrained capability state, and a
quiet progress rail. The compact capture proves that navigation labels collapse
into a centred icon rail rather than competing with the setup decision. No
clipping or inert enabled control was observed in the two captured states.

The visual direction now explicitly applies Apple's public design guidance as a
reference-only quality lens: strong hierarchy, direct language, agency,
recoverability, restraint, and careful detail. The implementation remains an
independent Windows-native design using Soltex tokens; no Apple asset, font,
measurement, material, component geometry, copy, code, or trade dress entered
the repository. The dated source and disposition record is in
`RESOURCE_PROVENANCE.md` beside this report.

## Nonclaims and remaining gates

- The two captures do not prove live per-monitor DPI, keyboard-only, Narrator,
  high-contrast, light-theme, reduced-motion, or human acceptance.
- This slice does not implement or prove Mica, AppWindow activation, a custom
  native title bar, notifications, Capture, Sonar-aware Audio, DSP, Advanced Lab
  recipes, MSIX lifecycle, or public release.
- The six Stage 6 Whisper physical checks remain unresolved and preserved.
