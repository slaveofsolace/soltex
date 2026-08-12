# Soltex handoff

Snapshot: 2026-08-12 (America/Chicago)

## Repository and ownership

```text
repository: slaveofsolace/soltex
pull request: #11 (draft)
branch: sol/soltex-product-rebuild
clean published base: abf1dc5e58ca7a23ef57a975c7fdeb041ea4d183
exact owner-host source: 4207ecb70ef30c09203cb4f0f2b3efedf1ef2bd6
isolated implementation worktree: C:\Users\suhai\Documents\soltex-product-rebuild
protected owner checkout: C:\Users\suhai\Documents\SOL Tools
protected owner branch/head: main / bf2662de80992cfed761625642f084d3caaa0f04
```

The protected owner checkout is a physical directory with substantial pre-existing user/mixed dirty work. It has not been reset, cleaned, stashed, discarded, overwritten, or used for rebuild edits. All current product changes are task-owned in the isolated worktree.

No task-owned build, test, render-smoke, runtime-probe, Soltex, or PowerShell process remained active after the last probe.

## Accepted product slices

### Activity

PR head `4437db80b844dcfe33cd8265e41cb5fcf00bfd76` has accepted owner-host and hosted Windows/package evidence. Activity is a bounded searchable timeline of meaningful Soltex actions and recovery transitions. It defaults to session-only memory, offers explicit 7-day/30-day retention, sanitizes path-like text, bounds storage to 120 entries/256 KiB, uses atomic persistence, and requires confirmation before shortening retention or clearing history.

### Audio

Owner-host exact source `3511ba92bde450ffac4e3fad145d9a13b8986e72` and published reconciliation head `abf1dc5e58ca7a23ef57a975c7fdeb041ea4d183` have accepted evidence. The slice adds bounded active shared-mode Windows Core Audio sessions, guarded per-session volume/mute, target revalidation, and immediate read-back. System, transferred, ended, or process-unverifiable sessions remain read-only. Raw identifiers, paths, command lines, and icon paths are neither exposed nor persisted.

Hosted Windows run `31562540695` and package-smoke run `31562540694` passed. Downloaded artifacts `9128308988` and `9128281441` independently matched their GitHub digests. All 14 retained PNG identities revalidated; default/expanded Audio and packaged Home were directly inspected. The package executable launched with exit code 0 and remained unsigned. Optional hosted AMSI/EICAR remained 31/32 because the installed provider returned native result `1`; required gates passed and no efficacy claim is made.

### Services, runtime lifecycle, and notification area

Exact owner-host source `4207ecb70ef30c09203cb4f0f2b3efedf1ef2bd6` adds the next system-lifecycle slice:

- a third Applications tab for bounded, searchable, read-only Win32 Services;
- query-only SCM rights, driver exclusion, no binary paths, and no service mutation;
- a schema-2 preference migration with default Exit and explicit Notification-area close behavior;
- direct `Shell_NotifyIconW` notification-area handling with only Open Soltex and Exit Soltex, taskbar recreation recovery, explicit icon/menu ownership, and fail-closed restoration of a hidden window;
- no installed service or hidden executor;
- cancellation of Performance sampling when hidden, minimized, or closing;
- released navigation animation clocks and cleared workspace animations before minimize;
- a fresh-output runtime probe with exact commit identities, state-separated CPU/memory/lifecycle samples, per-thread attribution, and 18-transition navigation timing;
- a fifteenth native evidence state for progressively disclosed Services;
- required runtime-cost evidence in the Windows workflow;
- startup and active-operation ownership that drains before disposing security resources;
- controlled render/probe evidence that cannot exit successfully without confirmed resource disposal.

Exact owner-host checks are green:

```text
Release build: 0 warnings / 0 errors
identity: 192 tracked text files / 9 reasoned allowlist entries
Security/EICAR: 32/32
Monitoring: 16/16
Audio: 20/20
Soltex.App.Tests: 26/26
live Services: Current; 296 exposed / 296 observed / 0 inaccessible / 0 omitted; 92.8 ms
startup: 2997.4 ms
navigation: 18 transitions; 39.1 ms mean / 379.2 ms maximum
visible idle CPU: 0.643% normalized
minimize transition CPU: 2.531% normalized
minimized steady CPU: 0.000% normalized
hidden notification-area CPU: 0.000% normalized
native matrix: 15/15; no error sidecars
package: 71,583,560 bytes; sha256 761822A48D3FD9A56A99E91C9368FA4AD513777BB2C44A6E0563C4BE4A445459; launch 0; NotSigned
```

The transition and steady numbers are deliberately separate. The one-time minimize transition was attributed to a worker/runtime thread, not the WPF dispatcher. Short samples are regression evidence, not hardware benchmark scores.

Exact evidence:

```text
C:\Users\suhai\.codex\visualizations\2026\08\12\soltex-product-rebuild\lifecycle-tray-exact-4207ecb-20260812-0055
```

The user-reported `QuarantineStore` disposed-object dialog was a real shutdown race. It is fixed at `4207ecb`: accepted shutdown drains owned work before disposal; a drain miss skips disposal and reports failure; controlled evidence waits for initialized state and confirmed cleanup. A complete diff-focused security scan has zero surviving findings. The false-success cleanup candidate found during review was corrected and retained as rejected audit row `SOLTEX-SHUTDOWN-001`.

## Exact resume step

1. Commit this exact evidence-ledger reconciliation without changing the accepted source.
2. Push `sol/soltex-product-rebuild` without force and update draft PR #11.
3. Wait for exact published Windows/package workflows.
4. Download and re-hash artifacts, verify PR merge ancestry, and inspect hosted Services/Security/package pixels.
5. Record hosted acceptance or the exact remaining blocker before beginning the next bounded product slice.

## Remaining product work

- hosted acceptance plus owner visual review for the Services/runtime/notification-area slice;
- supported default/fallback audio endpoint selection, still separate from routing or DSP;
- bounded historical numeric telemetry with retention, migration, and measured storage/runtime cost;
- deeper startup/driver observation without generic cleanup claims;
- mTLS/private-mesh device enrollment, revocation, emergency stop, and isolated personal/work connectors;
- Windows Graphics Capture and benchmark provenance/cancellation with measured game impact;
- signed distribution plus install/repair/upgrade/rollback/uninstall evidence;
- keyboard/UI Automation, high contrast, reduced motion, 100-200% scaling, representative viewport, and owner visual acceptance matrices.

Soltex is a Windows-native personal system workspace. It is not a SteelSeries GG/Sonar replacement, AppControl replacement, registered antivirus, unattended correction platform, production remote-management agent, signed public release, or production-ready.
