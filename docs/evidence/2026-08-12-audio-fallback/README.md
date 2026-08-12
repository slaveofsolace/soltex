# Audio fallback and telemetry-lifecycle gate

Snapshot: 2026-08-12

Owner-host production source: `1d15071a14472dce199796a57966bddbf7be2fc4`

Owner-host strengthened test head: `41c8c37622ef666436024138e6d82f14e1e418da`

Hosted Windows/package acceptance: pending publication

This gate covers the bounded Audio device-handoff slice and the shutdown race exposed by its first exact native evidence run. Soltex observes Windows-reported defaults, stores optional playback/recording fallback reminders as direction-scoped fingerprints, and opens the fixed Windows Sound Settings URI. It does not assign default endpoints, route audio, perform automatic failover, create virtual devices, or apply DSP.

## Exact functional gate

The owner-controlled Windows host used private .NET SDK 10.0.302. No Windows security setting, antivirus configuration, endpoint assignment, third-party audio session, service, startup entry, or network listener was changed.

| Gate | Result |
|---|---:|
| Release solution build | Passed; 0 warnings / 0 errors |
| identity policy | Passed; 194 tracked text files / 9 reasoned allowlist entries |
| design-token policy | Passed; 10 XAML files |
| `Soltex.Security.Tests -RunEicar` | 32/32 |
| `Soltex.Monitoring.Tests` | 16/16 |
| `Soltex.Audio.Tests` | 21/21 |
| `Soltex.App.Tests` | 29/29 on strengthened test head |
| native matrix | 16/16 at 1280x820; no error sidecars |
| runtime lifecycle | Passed; exact source/tested identities recorded |
| self-contained package | Two native renders exited `0`; exact identity recorded |

The EICAR fixture is a harmless provider-interoperability marker. It is not malware-efficacy, comparative-engine, or production-readiness evidence.

## Failure and recovery evidence

The first exact matrix attempt at source `975e48ceb543986f33145bbd141e52d88693c701` captured its first Home frame and then failed the process with:

```text
System.ObjectDisposedException: The CancellationTokenSource has been disposed.
```

The failed run was not accepted or overwritten. Its negative-evidence root is retained separately. Review found two telemetry-token disposal owners: the window close path and an already-queued visibility reconcile could both retain the same cancellation source. Source `1d15071` moves the token and task behind one serialized `TelemetryLoopOwner`; only `StopAsync` clears, cancels, awaits, and disposes them under the same gate.

The focused regression uses cancellation/release barriers to prove duplicate stops wait for the in-flight owner and a queued restart cannot publish a new loop before prior cleanup completes. Three working-tree render repetitions passed before source freeze. The complete exact-source matrix then launched and shut down 16 independent app processes without reproducing the exception. An independent full-file lifecycle review found no blocking security or correctness candidate in the final production ownership path.

## Audio privacy and admission boundary

- The raw Windows endpoint identifier remains internal to the provider.
- A direction-scoped SHA-256 fingerprint is limited to 64 lowercase hexadecimal characters.
- Endpoint-ID input is bounded to 1,024 characters before hashing.
- Preference schema 3 stores at most one playback and one recording fingerprint; invalid values recover to empty while supported settings remain intact.
- The Mixer admits only active endpoints with canonical fingerprints; the main window independently repeats active-state, direction, and canonical-fingerprint validation before persistence.
- The Windows Settings launcher has one constant target, `ms-settings:sound`, with no user-controlled executable, URI, argument, or shell text.

A focused security-diff review found no surviving candidate in the new process-launch, fingerprint, preference, or UI-to-persistence boundaries. This is scoped review evidence, not a repository-wide vulnerability guarantee.

## Exact runtime evidence

Runtime schema 2 bound both identities to `1d15071a14472dce199796a57966bddbf7be2fc4`.

| Measure | Observed |
|---|---:|
| probe PID | 45500 |
| startup completion | 2,036.1 ms |
| navigation | 18 transitions; 15.7 ms mean / 125.4 ms maximum |
| visible idle | 0.576% normalized CPU; Performance sampler active |
| minimize transition | 1.664% normalized CPU; Performance sampler suspended |
| minimized steady | 0.022% normalized CPU; Performance sampler suspended |
| hidden notification area | 0.000% normalized CPU; Performance sampler suspended |

CPU is normalized across 32 logical processors. These short owner-host samples are regression measurements, not benchmark scores or cross-machine guarantees.

## Native inspection

The exact 16-state manifest binds source and tested commit to `1d15071`. Default Mixer, disclosed device management, and full endpoint inventory were directly inspected at original 1280x820 resolution. The default surface prioritizes five controllable app sessions and a compact endpoint summary. Reminder controls and the first endpoint rows appear only after **Manage devices**; the remaining 50 endpoint rows appear only after a second explicit disclosure. Buttons, disabled states, partial-observation status, scroll regions, and text resources rendered correctly.

```text
Mixer default: 177,330 bytes / sha256:280b0b248b06857f9e2698b5aa27958dcd1d9f39dc8f13af84361e51861200a4
Mixer devices: 163,576 bytes / sha256:38d9487a57d35056055419d5791ff84199f1f51bf19ec14b56cc946ec1e80938
Mixer full inventory: 222,751 bytes / sha256:1e71073e3d386f5e6eb62ae2b0e5b2388fae250345deb62c49df7bf975503565
```

This is agent inspection of one software-rendered viewport, not owner visual acceptance, keyboard/accessibility conformance, scaling validation, high-contrast validation, or hardware-rendering acceptance.

## Exact package gate

```text
file: Soltex.exe
length: 71,589,791 bytes
sha256: 83A0A29347D8160DCD86F41AD828BD444EB3EC3DFB45B3317F8A3DDF092C9911
Authenticode: NotSigned
Home render: 106,907 bytes
Home render sha256: AC3693FAEBA6AB2302DFAB8238C7BAC96A4C98A91D880B0126A3AB2606406DC7
Mixer-device render: 163,576 bytes
Mixer-device render sha256: 38D9487A57D35056055419D5791FF84199F1F51BF19EC14B56CC946EC1E80938
both launch exits: 0
error sidecars: absent
```

The packaged Mixer-device pixels were directly inspected and match the intended progressive-disclosure state. The executable is unsigned and must not be represented as a trusted public distribution.

## Evidence roots

```text
accepted: C:\Users\suhai\.codex\visualizations\2026\08\12\soltex-product-rebuild\audio-fallback-exact-1d15071-20260812-0200
negative: C:\Users\suhai\.codex\visualizations\2026\08\12\soltex-product-rebuild\audio-fallback-exact-975e48c-20260812-0145
```

## Remaining gate

Publish the reconciliation head, require exact hosted Windows and package-smoke workflows, independently re-hash downloaded artifacts, verify source/tested ancestry, and inspect retained hosted Mixer/package pixels. Owner visual acceptance, signed distribution, accessibility/scaling, install/update/uninstall lifecycle, automatic audio switching, routing, and processing remain open.
