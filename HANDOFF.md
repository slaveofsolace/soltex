# Soltex handoff

Snapshot: 2026-08-11 (America/Chicago)

## Repository and ownership

```text
repository: slaveofsolace/soltex
pull request: #11 (draft)
branch: sol/soltex-product-rebuild
validated Activity PR head: 4437db80b844dcfe33cd8265e41cb5fcf00bfd76
local Activity evidence reconciliation: ce584cfdf6b62085f6b5bd45280dd92fb428e72d
validated Audio source head: 3511ba92bde450ffac4e3fad145d9a13b8986e72
evidence reconciliation: current documentation commit, pending publication
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

## Completed Audio candidate

- Added bounded active shared-mode render-session discovery through documented Windows Core Audio interfaces.
- Inspects at most 128 session slots and exposes at most 24 active, path-free app rows.
- Keeps system-sounds, multi-process/transferred, ended, and process-unverifiable sessions read-only.
- Holds only one-way endpoint/session identities in memory; raw identifiers, executable paths, command lines, and icon paths are not exposed, persisted, or logged.
- Revalidates endpoint/session identities plus process ID/start time immediately before each explicit volume/mute request.
- Requires immediate Windows volume/mute read-back before showing success and exposes rejection, target drift, unavailability, and mismatch separately.
- Makes app sessions the primary Audio job and moves full device inventory behind explicit progressive disclosure.
- Records only the sanitized result of a user-owned request as meaningful Activity.
- Preserves explicit nonclaims for endpoint switching, routing, virtual devices, EQ, DSP, noise suppression, microphone processing, profiles, and Sonar parity.

Exact-source owner-host evidence is green: full Release/identity/design gates, Security/EICAR 32/32, supply chain 18/18, hardening 12/12, Updates 17/17, Device Fabric 24/24, Monitoring 16/16, standard Audio 20/20, opt-in controlled silent-session write/read-back 21/21, and App/control 19/19. The controlled write touched only a task-owned silent WinMM session and wrote its already-observed values back unchanged. The complete native matrix passed 14/14 at 1280x820; default and expanded-device Audio were directly inspected. Self-contained package identity and launch/render passed; the executable is truthfully unsigned. Hosted exact-head/package acceptance is still pending.

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

Audio source `3511ba92bde450ffac4e3fad145d9a13b8986e72` owner-host evidence:

```text
functional logs:
C:\Users\suhai\.codex\visualizations\2026\08\11\soltex-product-rebuild\audio-exact-3511ba9-20260811-230454

14-state native packet:
C:\Users\suhai\.codex\visualizations\2026\08\11\soltex-product-rebuild\audio-native-3511ba9-20260811-230647

self-contained package packet:
C:\Users\suhai\.codex\visualizations\2026\08\11\soltex-product-rebuild\audio-package-3511ba9-20260811-230909
```

The package executable is 71,559,764 bytes with SHA-256 `31ef5e44b895738723b849705247fb2a4756a2a2092ec9aa514eea429aa64ebb`; the package Home render is 1280x820 and launch exited `0`. This is unsigned launch evidence, not trusted-distribution or installer-lifecycle evidence.

## Exact resume step

1. Commit this evidence-only reconciliation without changing validated source behavior.
2. Push `sol/soltex-product-rebuild` and wait for both draft-PR Windows and package-smoke workflows on the exact published reconciliation head.
3. Download and re-hash retained artifacts, validate source/tested commit ancestry, and inspect hosted default/expanded Audio pixels before acceptance.
4. After hosted acceptance, continue with service/startup health plus notification/background-runtime policy and idle/minimized/navigation-cost measurements.

## Remaining product work

- hosted/package acceptance and owner visual review for the per-app Core Audio candidate;
- supported default/fallback endpoint selection, still separate from routing or DSP;
- bounded historical numeric telemetry with independently proven retention/migration/cost;
- notification/background-runtime policy and idle/minimized/navigation-churn measurements;
- service/driver/startup health observation through supported Windows boundaries;
- enrolled mTLS/private-mesh agents with revocation, emergency stop, and separate personal/work permission domains;
- Windows Graphics Capture, benchmark provenance/cancellation, and measured game impact;
- signed distribution and install/repair/upgrade/rollback/uninstall evidence;
- keyboard/UI Automation, high contrast, reduced motion, 100–200% scaling, representative viewport, and owner visual acceptance matrices.

Soltex remains a Windows-native personal system workspace. It is not a SteelSeries GG/Sonar replacement, AppControl replacement, registered antivirus, unattended correction platform, production remote-management agent, signed public release, or production-ready.
