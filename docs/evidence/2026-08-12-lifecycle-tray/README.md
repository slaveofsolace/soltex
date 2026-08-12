# Lifecycle, Services, and notification-area gate

Snapshot: 2026-08-12

Source and tested commit: `4207ecb70ef30c09203cb4f0f2b3efedf1ef2bd6`

This gate closes the owner-host acceptance portion of the bounded Services/runtime slice and the user-reported shutdown regression:

```text
Cannot access a disposed object.
Object name: 'Soltex.Security.QuarantineStore'.
```

The source commit adds query-only Windows service observation, explicit Exit/default and Notification-area/opt-in lifecycle behavior, a direct `Shell_NotifyIconW` notification resource, suspended performance sampling while minimized or hidden, and explicit asynchronous ownership through shutdown. Controlled evidence waits for full initialization and requires a confirmed resource-disposal result before it can exit successfully.

## Exact functional gate

The owner-controlled Windows host used the repository contract through private .NET SDK 10.0.302. No Windows security setting, antivirus registration, exclusion, endpoint assignment, service configuration, startup entry, or third-party process was changed.

| Gate | Result |
|---|---:|
| Release solution build | Passed; 0 warnings / 0 errors |
| identity policy | Passed; 192 tracked text files / 9 reasoned allowlist entries |
| `Soltex.Security.Tests -RunEicar` | 32/32 |
| `Soltex.Monitoring.Tests` | 16/16 |
| `Soltex.Audio.Tests` | 20/20 |
| `Soltex.App.Tests` | 26/26 |
| live read-only Services | Current; 296 exposed / 296 observed / 0 inaccessible / 0 omitted; 92.8 ms during the exact verifier |
| native matrix | 15/15 at 1280x820; no error sidecars |
| self-contained package Security render | Exit `0`; no error sidecar |

The EICAR case is the standard harmless marker and confirms interoperability with the registered provider on this host. It is not malware-efficacy, comparative-engine, or production-readiness evidence.

## Shutdown ownership correction

Accepted close now cancels the active operation and telemetry loop, waits up to 20 seconds for active operation, startup, and telemetry ownership to drain, and only then disposes the import monitor, protection monitor, update journal, and security runtime. If the drain misses its bound, Soltex deliberately leaves resources undisposed for process termination rather than racing disposal against live work.

The first security review pass found that this abandoned-cleanup path still raised a success-shaped completion event. The final source carries `ResourcesDisposed=false` for that path and `true` only after disposal. Normal shutdown maps false to exit code `1`; controlled render/runtime modes additionally require an observed, completed, true result and write an error sidecar otherwise. Focused tests independently reject false, missing, and pending cleanup signals.

A canonical Codex Security working-tree scan validated with complete coverage and zero surviving findings. The rejected candidate remains in its audit ledger as `SOLTEX-SHUTDOWN-001`. The scan bundle remains in its task-owned scan directory; it was not silently copied into repository retention.

## Exact runtime evidence

Runtime schema 2 bound both source and tested identities to `4207ecb70ef30c09203cb4f0f2b3efedf1ef2bd6`.

| Measure | Observed |
|---|---:|
| probe PID | 27020 |
| startup completion | 2,997.4 ms |
| navigation | 18 transitions; 39.1 ms mean / 379.2 ms maximum |
| visible idle | 0.643% normalized CPU; Performance sampler active |
| minimize transition | 2.531% normalized CPU; Performance sampler suspended |
| minimized steady | 0.000% normalized CPU; Performance sampler suspended |
| hidden notification area | 0.000% normalized CPU; Performance sampler suspended |

The minimize transition and steady-state samples remain separate. These short local samples are regression measurements, not a hardware benchmark or cross-machine guarantee.

## Native inspection

The exact 15-state manifest binds source and tested commit to `4207ecb`. Overview, Services, Security, and Settings were directly inspected after capture. They show initialized live state, quiet progressive disclosure, working scroll regions, no gross clipping, no broken glyph/resource, and no exception dialog. This is agent inspection of one software-rendered 1280x820 viewport, not owner visual acceptance, scaling validation, accessibility conformance, or hardware-rendering acceptance.

```text
Overview: 107119 bytes / sha256:ed17919ac008a5ed39883f6100eba36a56085e796a154922cc7424e20a853ebc
Services: 176729 bytes / sha256:23fbef77de7792526da2548f28c64c1cf18771722ffcf9d35bb0866abbfeb194
Security: 134138 bytes / sha256:bd38259ea515753e965b9ede47bc0f2b9080c10fd02ce65f7d573e048440b8f9
Settings: 121020 bytes / sha256:347ec45d60f33120d0204e724049481dd6c1663329030d38fdc15bcd418ce9a3
```

## Exact package gate

```text
file: Soltex.exe
length: 71,583,560 bytes
sha256: 761822A48D3FD9A56A99E91C9368FA4AD513777BB2C44A6E0563C4BE4A445459
Authenticode: NotSigned
Security render: 132,809 bytes
Security render sha256: ADD4CE9C80B5574E3B250886EF2A7B2992C6AB57FEE08B6D2AA2248FEA1E4E9A
launch exit: 0
error sidecar: absent
```

Replacing the Windows Forms notification resource with direct shell interop removed about 10.55 MiB from the earlier self-contained comparison package while preserving focused native-lifecycle tests. The package is unsigned and must not be represented as trusted public distribution.

## External evidence root

```text
C:\Users\suhai\.codex\visualizations\2026\08\12\soltex-product-rebuild\lifecycle-tray-exact-4207ecb-20260812-0055
```

## Remaining gate

The branch and draft PR still require exact published Windows/package workflows, downloaded-artifact digest checks, PR merge-ancestry verification, and hosted pixel inspection. Owner visual acceptance also remains open.
