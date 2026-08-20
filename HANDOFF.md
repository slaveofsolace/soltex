# Soltex handoff

Snapshot: 2026-08-12 (America/Chicago)

## Repository and ownership

```text
repository: slaveofsolace/soltex
pull request: #11 (draft)
branch: sol/soltex-product-rebuild
accepted published evidence head: 6e54fb509ba332191107aa64733db0880e3cac78
latest accepted owner-host source: 1d15071a14472dce199796a57966bddbf7be2fc4
latest strengthened test head: 41c8c37622ef666436024138e6d82f14e1e418da
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
<local-evidence-root>\2026\08\12\soltex-product-rebuild\lifecycle-tray-exact-4207ecb-20260812-0055
```

The user-reported `QuarantineStore` disposed-object dialog was a real shutdown race. It is fixed at `4207ecb`: accepted shutdown drains owned work before disposal; a drain miss skips disposal and reports failure; controlled evidence waits for initialized state and confirmed cleanup. A complete diff-focused security scan has zero surviving findings. The false-success cleanup candidate found during review was corrected and retained as rejected audit row `SOLTEX-SHUTDOWN-001`.

Published source head `6e54fb509ba332191107aa64733db0880e3cac78` has accepted hosted evidence. Windows run `31568771869` and package-smoke run `31568771855` completed successfully. Downloaded artifacts `9130547242` and `9130511337` matched GitHub digests exactly; all 15 retained PNG identities and 1280x820 dimensions revalidated. GitHub confirms tested PR merge `ebd163553b3229099c371cd79b8967ace2b1ab55` is exactly one commit ahead with the source as merge base. Services, Security, Settings, and packaged Home were directly inspected without the reported dialog. The hosted package launched `0`, remained `NotSigned`, and matched SHA-256 `236959aae3abe37a35130b68515c1472730118a6e6c8f60c9315f1ca0107e8b9`. Optional hosted EICAR/AMSI remained 31/32 because the runner provider returned result `1`; no efficacy claim is made.

### Audio defaults and fallback reminders (owner-host accepted; hosted pending)

Production source `1d15071a14472dce199796a57966bddbf7be2fc4` adds a bounded, user-mediated device workflow without claiming a system-default setter:

- active endpoint IDs are reduced to direction-scoped 64-character fingerprints; raw IDs never reach the public model or preference file;
- preference schema 3 stores at most one playback and one recording fallback reminder and migrates older schemas to empty reminders;
- only an active, fingerprinted endpoint can be remembered; missing/invalid values remain visibly unset or unavailable;
- **Manage devices** progressively discloses reminder controls and complete endpoint inventory;
- **Windows Sound** dispatches only the fixed `ms-settings:sound` URI; Windows and the user own the actual default-device choice;
- no undocumented `IPolicyConfig`, automatic switching, routing, virtual device, DSP, or background audio worker is introduced;
- the evidence matrix adds `mixer-devices` separately from the existing full `mixer-more` inventory state.

Owner-host acceptance passed a zero-warning Release build, identity/design policy, Security/EICAR 32/32, Monitoring 16/16, Audio 21/21, App/control 29/29 on strengthened test head `41c8c37`, 16/16 native states, an exact runtime probe, and self-contained Home/device renders. The first exact native attempt exposed and preserved a disposed telemetry-token race; the accepted source serializes start/stop/disposal and completed the full matrix without recurrence. Default, device-management, full-inventory, and packaged-device pixels were directly inspected. The package is 71,589,791 bytes, SHA-256 `83a0a29347d8160dcd86f41ad828bd444eb3ec3dfb45b3317f8a3ddf092c9911`, and truthfully `NotSigned`. Full evidence is in `docs/evidence/2026-08-12-audio-fallback/README.md`. Hosted acceptance remains pending.

## Exact resume step

1. Commit the exact cold-package recovery evidence ledger; production recovery source is `03595226d809b75ac40afeb2ba7b1c1126dfac33`.
2. Publish by fast-forward and require corrected-head hosted Windows/package workflows.
3. Verify source/tested ancestry, independently re-hash corrected artifacts, inspect hosted Mixer/package pixels, and update draft PR #11 metadata.
4. Preserve failed package run `31572129578` and artifact `9131786253` as negative evidence; do not relabel it as accepted.
5. Preserve the protected owner checkout, Windows-owned default-selection boundary, and all routing/DSP/owner-acceptance nonclaims.

## Remaining product work

- owner visual review for the Services/runtime/notification-area slice;
- hosted and owner acceptance for the user-mediated audio default/fallback workflow;
- bounded historical numeric telemetry with retention, migration, and measured storage/runtime cost;
- deeper startup/driver observation without generic cleanup claims;
- mTLS/private-mesh device enrollment, revocation, emergency stop, and isolated personal/work connectors;
- Windows Graphics Capture and benchmark provenance/cancellation with measured game impact;
- signed distribution plus install/repair/upgrade/rollback/uninstall evidence;
- keyboard/UI Automation, high contrast, reduced motion, 100-200% scaling, representative viewport, and owner visual acceptance matrices.

Soltex is a Windows-native personal system workspace. It is not a SteelSeries GG/Sonar replacement, AppControl replacement, registered antivirus, unattended correction platform, production remote-management agent, signed public release, or production-ready.
