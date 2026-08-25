# Overview, Performance, and product-copy slice

## Scope

This slice starts from `b95a0790658238be912ef24575ac69ead454d12d` on
`sol/soltex-native-instrument-v1`. It changes only the isolated finalization
worktree. The protected owner checkout was not edited.

The visual direction is calm, precise Windows-native instrumentation:

- one primary instrument per workspace;
- negative space and quiet separators instead of a repeated card grid;
- Segoe and the existing semantic colour/token system;
- short state-and-action copy on default surfaces;
- technical implementation detail kept in tests and evidence rather than
  exposed as product copy.

Apple Human Interface Guidelines were used only as a clean-room compositional
reference for restraint and hierarchy. Windows Fluent, Mica, native controls,
system typography, High Contrast, and Windows accessibility behaviour remain
authoritative. No competitor assets, code, or trade dress were imported.

## Product changes

- Overview is an unframed processor instrument with a compact machine rail,
  network timeline, and current-load region.
- Performance uses synchronized processor, memory, and network instruments.
  Storage and process actions remain progressively disclosed under System
  detail; Benchmark remains a separate explicit mode.
- Applications no longer exposes collection timing or implementation
  provenance in its footer.
- Devices replaces an internal six-item capability ledger with three plain
  trust states: system status, remote control, and files/storage.
- Security suppresses normal startup chatter and shows event count without
  query duration.
- Whisper uses `Setup` rather than `Scaffold` and its controlled render no
  longer exposes evidence-harness language.
- Audio omits zero-value diagnostic counters and reduces the default endpoint
  summary to actionable state.
- Updates, Capture, Remote Assist, Settings, and unavailable/recovery states use
  concise user language while preserving honest capability limits.

## Deletion-first review

Disposition after the final dark, light, and High Contrast renders:

- `DELETE`: repeated startup activity, capture-duration readouts, runtime
  framework trivia, redundant preview badges, internal capability-ledger rows,
  and the “no recorder / no overlay / no injection” slogan line.
- `REPLACE`: “bounded provider,” “provider provenance,” “candidate evidence,”
  “private staging token,” “deterministic render evidence,” and “scaffold” with
  direct state or action copy.
- `KEEP`: measured telemetry, source/publisher/version tables, explicit
  unavailable states, update signing boundary, RustDesk signature fingerprint,
  quarantine actions, and Whisper fail-closed readiness.
- `VERIFY`: physical keyboard-only and Narrator review, live native caption and
  snap behaviour, owner visual acceptance, and live-workload performance remain
  separate gates.

The tracked-source design-tell scan reported zero candidates after excluding
generated `obj` output. The visual result has no P0/P1 generic-template tell
identified in the current renders. This is an agent visual assessment, not
human acceptance.

## Focused verification

| Check | Result | Artifact | SHA-256 |
| --- | --- | --- | --- |
| Release application-test build | 0 warnings, 0 errors | `artifacts/native-instrument-v1/overview-performance/copy-cleanup-build-final-2.log` | `b967b44dcdefe2cf1e4cf38cec713514f76d03b36e656d78616350c32331575b` |
| Application regression suite | 53/53 passed | `artifacts/native-instrument-v1/overview-performance/copy-cleanup-app-tests-final-2.log` | `fe57acd9e864014203087dc88601e34a03c28029f9d7f5dbce93c3e3144ca840` |
| Canonical solution Release build | 0 warnings, 0 errors | `artifacts/native-instrument-v1/overview-performance/copy-cleanup-solution-release.log` | `1d51b318a60caf250197b2e4b5252366061be2ea7021cd9a7e4ce58152b16730` |
| Post-build application regression suite | 53/53 passed | `artifacts/native-instrument-v1/overview-performance/copy-cleanup-app-tests-canonical.log` | `9bc38169f6026d61a522b694bb923149ef74d41c84ce565ec430cad9d313cf3d` |
| Design-token and identity gates | 13 XAML and 331 tracked text files passed | `artifacts/native-instrument-v1/overview-performance/copy-cleanup-policy-gates-final.log` | `dad14ac7f6e3887dbb16d9b656eb54fb8093c96f112a83a54a185255d02a2918` |
| Tracked UI design-tell scan | 0 candidates; human visual inspection still required and performed | `artifacts/native-instrument-v1/overview-performance/tracked-ui-design-tell-scan.log` | `877825cfe491bfcb62dc0e21584eaf2f465f289dc66ed3f17fb9554c48557d46` |
| Reasoning ledger schema | PASS 2.0.0 | `artifacts/native-instrument-v1/overview-performance/reasoning-ledger-validation.log` | `c8844f2a1e8d426c963bc69e0825b08f752bc26e0f808fe77a58e75cff87cc02` |

Final controlled renders captured from the same source state:

| Surface | Theme | SHA-256 |
| --- | --- | --- |
| Overview | Dark | `ded2d213d2814c6cad1d73ffa27d85dd7d7647c005946cec209e40e03c4de905` |
| Performance | Dark | `a94d8b5d4cb1eea919b33d54c2e8a5a509ca7e888761099eabdb148fd1e68f1c` |
| Applications | Dark | `9dd02f30a851991791a3371b54c900ebc9f6a5866d07c8ea999df80314888ce2` |
| Devices | Dark | `1834247a5e040107fce6a1b31729c42c2bd4b7be606e7595d38f4c1bfc303a5f` |
| Security | Dark | `e199c701dc406d85412bf557130c893ece8ba5154704e40b46421d8fe1cfb6f8` |
| Remote Assist | Dark | `15cfff5051b0a0444037232a2e887c038377902bff3e4702c85527196120c0ce` |
| Whisper | Dark | `7dd86450f62ab16ad69cb56e8198fa695e5d8744eab1381968ad08669e027d8e` |
| Updates | Dark | `7050c392c3d2ce364b5f8a1c8a07c703f8e99f4a701d69a4287db67911ce5115` |
| Capture | Dark | `699d1808acc9f80a79627b5c107cdaf2cd7c5ea74eef7711368f1f36d8ee7472` |
| Settings | Dark | `ae594b491514239367953e66b43804d6521b8e08d6ee38cde1daee33f413f3f0` |
| Audio | Dark | `985c893dd75dbdb587106488ca6c8e4a7140892c5c3f72d3fc0ce2ef0cc11553` |
| Overview | Light | `8464dfd3a57f81d24de2db283efeb5be6f98f84f7a7b32fe80e3179fc2c50971` |
| Performance | Light | `4d95eb821b26ef5522fd62fc19b75adcdc2746a9e24b5545aee5ba64dbd30c0b` |
| Overview | High Contrast | `439a7989bf4d653dd479129d296c05817882bf2082539a9aaba3d6f7e45172a1` |
| Performance | High Contrast | `583614e278c0262a35fcbc6bba6cad3f9a238340468b0c317e7b440f0041bd76` |

The exact PNG paths are under
`D:\soltex-native-instrument-v1\artifacts\native-instrument-v1\overview-performance`.
All controlled render processes exited successfully. Concurrent Security,
Devices, and Updates captures each exceeded the renderer startup bound once;
the single isolated retry for each exited zero. No runtime assertion was
weakened.

## External resource decision

No external implementation or visual asset was admitted in this slice. The
separate rights-reviewed candidate registry remains a decision input for later
Capture and Audio work. This UI slice uses only repository-owned source,
built-in WPF/Windows resources, and the already admitted Windows App SDK
dependency.

## Nonclaims

- Controlled client renders do not prove physical caption buttons, snap-layout
  hover, Mica translucency, live per-monitor DPI movement, keyboard-only
  operation, Narrator output, or screen-reader acceptance.
- The current Capture surface remains unavailable and makes no recording claim.
- Remote Assist remains a user-selected RustDesk handoff, not a bundled remote
  control service.
- The six outstanding owner-physical Whisper Stage 6 checks remain unresolved.
- No Store, App Installer, winget, installer, update, merge, or public release
  action occurred.

Captured at `2026-08-25T19:48:34.070Z`.
