# Soltex handoff

Snapshot: 2026-08-11 (America/Chicago)

## Repository and ownership

```text
repository: slaveofsolace/soltex
pull request: #11 (draft)
branch: sol/soltex-product-rebuild
validated Activity source head: 0edb327a46aa05a5cc5e804d30ac56cd797e210a
isolated worktree: C:\Users\suhai\Documents\soltex-product-rebuild
protected owner checkout: C:\Users\suhai\Documents\SOL Tools
protected owner branch/head: main / bf2662de80992cfed761625642f084d3caaa0f04
```

The protected owner checkout is a physical directory with substantial pre-existing legacy-identity modified and untracked work. It remains user/mixed-owned and was not reset, cleaned, stashed, discarded, overwritten, or used for the rebuild implementation. All current product work occurred in the isolated worktree.

No build, test, render-smoke, Soltex, or task-owned PowerShell process remained active at this checkpoint. The tree is reboot-safe.

## Completed Activity slice

- Added a first-class searchable Activity workspace under Maintenance.
- Records meaningful Soltex-owned actions and failure/recovery transitions, not clicks or browsing.
- Defaults to session-only memory and creates no Activity file.
- Adds explicit 7-day and 30-day per-account retention in Settings.
- Requires confirmation before shortening retained history or clearing visible/saved history.
- Bounds retained state to 120 entries and 256 KiB.
- Sanitizes control characters, whitespace, drive-root text, and UNC-like text in untrusted area/summary fields.
- Rejects unknown/invalid schemas, invalid JSON, oversized input, future/expired entries, and overflow.
- Uses same-directory temporary write plus atomic replacement and reports storage/deletion failure as `CHECK`.
- Connects meaningful Security, Performance/process-action, Remote Assist, quarantine, import-monitor, and recovery events while keeping the authenticated security audit log separate.
- Extends preferences, last-workspace restoration, tests, render-smoke routing, documentation, and the native evidence matrix.

The follow-up Settings polish removed an accidental clipped control edge at the canonical viewport without changing behavior.

## Verification

Owner-controlled Windows host, .NET SDK 10.0.302, Release configuration:

```text
solution build: passed
identity policy: passed (176 tracked text files; 9 reasoned allowlist entries)
Soltex.Security.Tests: 31/31
Soltex.Monitoring.Tests: 16/16
Soltex.App.Tests: 19/19
targeted post-polish app build: 0 warnings / 0 errors
targeted post-polish app tests: 19/19
exact-head native matrix: 14/14 at 1280x820
render manifest source/tested SHA: c88d995a89cbbb46ce481e21900927bf57192af6
```

Durable local evidence:

```text
final full verification transcript:
C:\Users\suhai\.codex\visualizations\2026\08\11\soltex-product-rebuild\activity-final-source-verify-20260811-221629.transcript.log

exact-head native packet:
C:\Users\suhai\.codex\visualizations\2026\08\11\soltex-product-rebuild\activity-native-c88d995-20260811-222807
```

The exact Activity and Settings captures were directly inspected. They show no gross clipping, broken assets, stock gradient/glass/card-grid treatment, or P0 generic-AI design tell. This is agent inspection at one software-rendered native viewport, not owner visual acceptance, accessibility conformance, DPI/scaling proof, or packaging evidence.

Exact PR head `4437db80b844dcfe33cd8265e41cb5fcf00bfd76` subsequently passed Windows run `31560242046` and package-smoke run `31560242019`. Windows artifact `9127496208` has digest `sha256:0046bc2b99c0e10accf58b3c030025c4802ceee782553a06f7c3881599e969c9`; package artifact `9127485263` has digest `sha256:dfd3b7aed215a2646bac1ad745e431511f5e4de4958b479b9a8d3865865ab30b`. The downloaded Windows ZIP independently matched its digest; its manifest bound source `4437db8` to tested PR merge `4b3fff8`, and all 14 retained PNG sizes, hashes, and 1280×820 dimensions revalidated. Hosted Activity and Settings pixels were directly inspected. The optional hosted AMSI/EICAR run remained 31/32 because the runner's provider returned native result `1`; required gates still passed, and no detection-efficacy claim is made.

## Exact resume step

1. Continue the next product-maturity slice: supported Windows Core Audio session observation on active render endpoints.
2. Bind sessions through `IAudioSessionManager2`/`IAudioSessionEnumerator`, expose sanitized process identity and current session volume/mute, and keep enumeration bounded.
3. Add explicit user-owned session volume/mute writes only with immediate Core Audio read-back and visible failure/recovery state.
4. Add focused COM-layout, bounds, identity-drift, disconnected-session, write/read-back, cancellation, live-overhead, and WPF interaction tests.
5. Capture and inspect the resulting Audio states before publication. Do not claim routing, EQ, DSP, noise suppression, virtual devices, or Sonar parity.

## Remaining product work

- per-app Core Audio session observation and supported read-back controls;
- bounded historical numeric telemetry with independently proven retention/migration/cost;
- notification/background-runtime policy and idle/minimized/navigation-churn measurements;
- service/driver/startup health observation through supported Windows boundaries;
- enrolled mTLS/private-mesh agents with revocation, emergency stop, and separate personal/work permission domains;
- Windows Graphics Capture, benchmark provenance/cancellation, and measured game impact;
- signed distribution and install/repair/upgrade/rollback/uninstall evidence;
- keyboard/UI Automation, high contrast, reduced motion, 100–200% scaling, representative viewport, and owner visual acceptance matrices.

Soltex remains a Windows-native personal system workspace. It is not a SteelSeries GG/Sonar replacement, AppControl replacement, registered antivirus, unattended correction platform, production remote-management agent, signed public release, or production-ready.
