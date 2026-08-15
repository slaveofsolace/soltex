# Audio session gate

Snapshot: 2026-08-11 America/Chicago

```text
repository: slaveofsolace/soltex
branch: sol/soltex-product-rebuild
validated audio source head: 3511ba92bde450ffac4e3fad145d9a13b8986e72
implementation worktree: C:\Users\suhai\Documents\soltex-product-rebuild
protected owner checkout: C:\Users\suhai\Documents\SOL Tools
protected owner branch/head: main / bf2662de80992cfed761625642f084d3caaa0f04
active task-owned process after gates: none
```

The owner checkout's pre-existing legacy/WaveSlate modified and untracked work remained untouched. All source, test, documentation, render, and package work occurred in the isolated rebuild worktree or the task evidence root.

## Implemented boundary

- Active shared-mode render sessions through `IAudioSessionManager2`, `IAudioSessionEnumerator`, `IAudioSessionControl2`, and `ISimpleAudioVolume`.
- At most 32 active render endpoints, 128 observed session slots, and 24 exposed active sessions.
- Control-character/whitespace sanitization, 80-character names, path-like display-name rejection, and no executable paths, command lines, icon paths, or public raw identifiers.
- Raw endpoint and session-instance identifiers reduced to process-memory-only SHA-256 identities.
- System-sounds, multi-process/transferred, ended, and process-unverifiable sessions remain read-only.
- Explicit volume/mute only after endpoint/session/process/start-time revalidation.
- Unique event context and immediate volume/mute read-back before success.
- Visible rejection, target drift, unavailability, and read-back mismatch.
- App controls first; complete endpoint inventory behind explicit progressive disclosure.
- Only sanitized user-action results enter Activity.

## Exact-head functional gate

All tracked processes used unique stdout/stderr logs and exact PIDs. The first verification invocation completed every product test green, but the tracker received a blank post-exit property and stopped before the additional suites. No process remained. One evidence-supported retry pinned the process handle before waiting; every tracked exit was `0`.

| Gate | Result |
|---|---:|
| identity policy | Passed; 181 tracked text files / 9 reasoned allowlist entries |
| design-token policy | Passed; 10 XAML files |
| Release solution build | Passed; 0 warnings / 0 errors |
| Security plus opt-in in-memory EICAR/AMSI | 32/32 |
| supply chain | 18/18 |
| hardening | 12/12 |
| Updates | 17/17 |
| Device Fabric | 24/24 |
| Monitoring | 16/16 |
| standard Audio | 20/20 |
| controlled silent-session Audio write/read-back | 21/21 |
| App/control | 19/19 |

The controlled Audio write gate created a task-owned one-second silent WinMM loop, identified only the test process's session, wrote its already-observed volume and mute values back unchanged, confirmed both through immediate Core Audio read-back, stopped the loop, and removed the temporary WAV. The normal automated suite remained read-only.

Latest measured owner-host observation in the exact gate: 5 active sessions exposed from 28 observed slots, 0 inaccessible, 0 omitted, 6.7 ms provider / 7.0 ms wall time. Endpoint observation remained partial because the host reports 62 bounded endpoint records plus 7 inaccessible/overflow records. These timings and counts are diagnostic, not guarantees.

Evidence root:

```text
<local-evidence-root>\2026\08\11\soltex-product-rebuild\audio-exact-3511ba9-20260811-230454
```

## Exact native matrix

The complete native matrix passed 14/14 with source and tested commit both equal to `3511ba92bde450ffac4e3fad145d9a13b8986e72`. Every PNG is 1280x820. The default and explicitly expanded-device Audio states were directly inspected.

```text
packet: <local-evidence-root>\2026\08\11\soltex-product-rebuild\audio-native-3511ba9-20260811-230647
default Audio: 166353 bytes / sha256:60bf70248d13511f01274c116da5ba02f1d7bb22199a59ad8a71bb3a20fd0828
expanded Audio: 197133 bytes / sha256:054b813954a695e36aace808fb785bec5d0a4d7e18ae8cbfd6575eea1353fe4e
```

Default Audio shows five active sessions, three controllable, one compact endpoint summary, and no overflow action in the page header. Complete endpoint density appears only after `View devices` and the additional-endpoint disclosure. No gross clipping, broken asset/glyph, generic gradient/glass treatment, or false success color was observed. `PARTIAL` accurately reflects inaccessible endpoint records. This is agent inspection at one software-rendered viewport, not owner acceptance or accessibility/scaling conformance.

## Exact package gate

```text
packet: <local-evidence-root>\2026\08\11\soltex-product-rebuild\audio-package-3511ba9-20260811-230909
file: Soltex.exe
length: 71559764
sha256: 31ef5e44b895738723b849705247fb2a4756a2a2092ec9aa514eea429aa64ebb
Authenticode: NotSigned
package Home render: 103725 bytes / 1280x820 / sha256:677908c321ef9aaaa7dafcb113969dd2e3218a1fea94716785ca33776e792c6a
```

This proves self-contained publish, exact identity, launch, and native rendering. It does not prove trusted distribution, signing, timestamping, install/repair/upgrade/rollback/uninstall, or SmartScreen reputation.

## Nonclaims and remaining gate

No endpoint switching, per-app routing, loopback, capture, virtual device, EQ, DSP, compression, spatial processing, noise suppression, microphone processing, profiles, or Sonar parity is claimed. Hosted Windows and package-smoke workflows must pass the published reconciliation head before this slice is accepted for merge. Owner visual acceptance, keyboard/UI Automation, high contrast, reduced motion, and 100-200% scaling remain open.
