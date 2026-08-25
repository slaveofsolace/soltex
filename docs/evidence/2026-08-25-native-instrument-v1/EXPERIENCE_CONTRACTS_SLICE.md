# Native Instrument V1 — experience contracts slice

Captured: 2026-08-25  
Source baseline: `a711d767699c6a5ddbeb289b196e4454452f66cc`  
Worktree: `D:\soltex-native-instrument-v1`

## Implemented

- Public, normalized `ThemeProfile` with System, Light, and Dark modes; Soltex
  Glacier or Windows accent; and Comfortable or Compact density.
- Public, fail-closed `FeatureCapability` states: Available, Degraded,
  Unsupported, and ConsentRequired.
- Versioned `OnboardingState` covering privacy/local data, appearance, audio
  discovery, Whisper, capture storage, shortcuts, and Advanced Lab safety.
- Transactional preference schema 5 migration. Existing mode, audio, activity,
  lifecycle, and workspace state is preserved; unknown new fields repair to safe
  defaults without replacing valid older fields.
- Live application-level accent and density application. Windows High Contrast
  remains authoritative. A Windows accent is contrast-corrected before it reaches
  interaction brushes; semantic status colours are unchanged.
- Working Settings controls for mode, accent, and density.

## Verification

| Gate | Result | Evidence | SHA-256 |
|---|---|---|---|
| Focused application suite | 51/51 pass, 1,897.1 ms | `artifacts/native-instrument-v1/experience-contracts/app-tests-final-2026-08-25T16-39-46-289Z.log` | `e7b984dd184481fcf7a1beecd3d858c6099b3543b6d82850cfde8491b1254b3a` |
| Release solution build | 0 warnings, 0 errors, 3.63 s | `artifacts/native-instrument-v1/experience-contracts/release-build-final-2026-08-25T16-40-19-646Z.log` | `0df9d35e1f3976ce22186891513b3a73c8f3b82079d124a9c4b83bd981db2c24` |
| Design-token policy | pass, 12 XAML files | `artifacts/native-instrument-v1/experience-contracts/design-tokens-final-2026-08-25T16-40-19-646Z.log` | `a2eb1eab1b0adaedad78ba03013e73890dac4acb173059e231a61d35bc07033b` |
| Identity policy | pass, 310 tracked text files | `artifacts/native-instrument-v1/experience-contracts/identity-final-2026-08-25T16-40-19-646Z.log` | `9a655bc898803ad4b286ba3cda1bfb8469b718828bd1fae4c7264b95657f45b1` |
| Focused native Settings render | pass, 1280×820, no error sidecar | `artifacts/native-instrument-v1/experience-contracts/render-2026-08-25T16-36-22-507Z/settings-current-source.png` | `d5dfde2d91bc601fe80a3e6f4c735fe804d26079348c6e3f3879922ff152d71e` |

`git diff --check` also passed.

## Nonclaims

- This slice does not prove Mica, native title-bar or snap-layout behavior,
  reduced-motion behavior, live per-monitor DPI, keyboard-only use, or Narrator.
- The onboarding contract is implemented and persisted; the guided first-run
  presentation is the next shell slice.
- Capture remains preview-only, Audio remains public-Core-Audio user-space
  control, and no system-wide Sonar or virtual-device parity is claimed.
- The six Whisper physical checks remain unresolved and unchanged.
