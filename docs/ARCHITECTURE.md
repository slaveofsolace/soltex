# Architecture

## Runtime shape

```text
Soltex.App (WPF, unelevated)
    |
    +-- Soltex.Monitoring
    |     +-- GetSystemTimes CPU observation
    |     +-- GlobalMemoryStatusEx physical-memory observation
    |     +-- bounded Process name/PID/CPU/memory/thread snapshots
    |     +-- bounded fixed-volume observation
    |     +-- immutable snapshots and copied bounded histories
    +-- Soltex.Audio
    |     +-- bounded MMDevice endpoint observation
    |     +-- active shared-mode render-session observation
    |     +-- in-memory one-way endpoint/session identities
    |     +-- guarded per-session volume/mute with immediate read-back
    +-- Soltex.DeviceFabric
    |     +-- sanitized local machine/runtime observation
    |     +-- explicit NotEnrolled state
    |     +-- exact six-capability non-executing policy model
    +-- Windows Security Center health (wscapi.dll)
    +-- Windows Security Center change signal (WscRegisterForChanges)
    +-- Defender status/scans/updates (fixed PowerShell commands)
    +-- Defender Operational event reader (fixed, bounded Get-WinEvent query)
    +-- Soltex.RemoteAssist
    |     +-- bounded RustDesk.exe discovery/selection
    |     +-- SHA-256 approval fingerprint
    |     +-- fixed external launch plans (no shell)
    |     +-- constrained peer-ID value object
    |           |
    |           +-- separately installed RustDesk UI/process
    +-- Soltex.Update
    |     +-- signed acquisition descriptors and trust rotation
    |     +-- pinned HTTPS transport and bounded private acquisition
    |     +-- signed manifest/archive/publisher composition
    |     +-- deterministic non-installing preview and confirmation
    |     +-- authenticated planning/recovery journal
    +-- Soltex.Security
          +-- protection monitor with retry/backoff/recovery state
          +-- AMSI intake scanner
          +-- file hashing and assessment
          +-- Authenticode verifier
          +-- detached signed-manifest verifier
          +-- exact-hash allow list
          +-- authenticated quarantine
          +-- HMAC-chained local audit log
          +-- bounded selected-product-root Imports watcher
```

Soltex installs no Windows service, kernel driver, browser extension, network proxy, or cloud backend in this release. Exit is the default close behavior. An explicit notification-area preference may keep the same desktop process open after its window closes; it does not create a service or hidden executor. A user-approved RustDesk process is external to Soltex and retains its own runtime behavior.

## Trust boundaries

- **Windows provider boundary:** `WindowsSecurityCenter` reads aggregate antivirus health. Explicit WSC `Good`, `Poor`, `Snoozed`, and `NotMonitored` states are authoritative. Defender detail flags are a fallback only when WSC is unavailable and Defender reports `Normal` with antivirus and real-time protection active. `PowerShellDefenderClient` launches the System32 Windows PowerShell host in its executable directory, imports Defender, Diagnostics, and Utility manifests through direct `$PSHOME` paths, module-qualifies every security cmdlet, invokes only fixed supported commands, and replaces raw error output with a bounded exit-code diagnostic before it reaches audit-facing models.
- **Native loading boundary:** the security assembly constrains its Windows P/Invoke resolution to System32. AMSI, WSC, WinTrust, Crypt32, and Kernel32 imports cannot be satisfied from the application or current directory.
- **Monitoring boundary:** `WindowsSecurityChangeMonitor` receives only a WSC change signal. `ProtectionMonitor` serializes refreshes, coalesces signals through a one-slot channel, uses one timeout-cancelled channel wait per cycle, polls at one-minute intervals, retries nonfatal failures with bounded exponential backoff, keeps the last successful observation, emits an explicit recovered state, and isolates subscriber failures so one UI observer cannot stop monitoring.
- **System-telemetry boundary:** `SystemTelemetryProvider` reads CPU timing from `GetSystemTimes`, physical memory from `GlobalMemoryStatusEx`, process summaries from `System.Diagnostics.Process`, and fixed-volume capacity from `DriveInfo`. One sample observes at most 2,048 processes, exposes at most 32 rows and eight volumes, limits names to 80 sanitized characters, and never reads executable paths. GPU and network readings are explicitly unavailable in this slice. The UI runs one sequential sample loop, cancels on shutdown, retains copied histories of at most 48/72 samples, marks retained values stale during two bounded retries, then becomes unavailable rather than fabricating a value.
- **Service-inventory boundary:** `WindowsServiceInventoryProvider` requests only SCM enumeration and service-configuration query rights, observes Win32 services while excluding drivers, exposes at most 512 path-free rows, and reads only state and start type into the model. It does not request service-control, change-config, delete, or install rights and never exposes binary paths or service accounts.
- **Audio boundary:** `AudioEndpointProvider` observes bounded MMDevice endpoint state. `AudioSessionProvider` inspects at most 128 active-render session slots and exposes at most 24 active shared-mode sessions without requesting executable paths, command lines, icon paths, or public raw identifiers. System-sounds, multi-process/transferred, ended, and process-unverifiable sessions remain read-only. An explicit volume/mute request re-enumerates and revalidates one-way endpoint/session identities, process ID, and process start time, uses a unique event-context GUID, and requires immediate `ISimpleAudioVolume` read-back before success.
- **Local-device boundary:** `LocalDeviceObservationProvider` reads only the bounded machine name and runtime/OS architecture descriptions. It reports `NotEnrolled`; it does not authenticate a device, discover peers, create an agent identity, listen on a socket, or turn the six-capability policy catalog into execution. The Devices surface routes Remote Assist requests to the existing visible external-client flow.
- **Event boundary:** the Defender Operational reader asks Windows for at most 24 recent allow-listed event IDs in the app and 100 at the library boundary, with a 512 KiB stdout byte ceiling enforced while the child stream is read. It treats only a no-matching-events condition as an empty result, surfaces other provider/access failures, and re-enforces the requested count after deserialization. `DefenderEventLogParser` constructs descriptions from an allow-list of fields and never exposes file, process, or scan-resource paths.
- **Content-intake boundary:** `FileAssessmentService` normalizes existing non-reparse files, hashes them, checks the exact-hash allow list, and sends at most 16 MiB to AMSI.
- **Release boundary:** `IntegrityManifestVerifier` verifies exact manifest bytes with RSA-PSS/SHA-256 before validating bounded relative paths, lengths, and hashes. `AuthenticodeVerifier` asks Windows to validate an individual signed file.
- **Update-planning boundary:** `UpdateDescriptorVerifier` requires exact signed descriptor bytes and metadata-key quorum before any network operation. `PinnedHttpsTransport` accepts only HTTPS with an active signed TLS SPKI pin; `BoundedHttpsAcquirer` constrains origins, redirects, time, lengths, hashes, and private reparse-free staging. `SoltexUpdatePlanner` composes signed-manifest, archive, publisher, and disk-impact evidence into one deterministic preview and exact expiring confirmation, then stops without installation or elevation.
- **Local-state boundary:** `ProductDataRootResolver` selects exactly one canonical or compatible product root, rejects dual roots, non-directory collisions, and reparse paths, and performs no implicit merge or copy. DPAPI protects a per-user HMAC key using the retained compatibility descriptor. Allow-list and quarantine indexes are authenticated. Quarantined payloads are re-hashed before restoration.
- **Privacy boundary:** audit records store an HMAC of a full path rather than the raw path. Update-journal detail is sanitized and the Updates page displays neither release URLs nor raw local paths. The local logs are authenticated/chained, though a same-user attacker can still truncate or delete local state.
- **Remote-assistance boundary:** Soltex never embeds the AGPL RustDesk runtime. It accepts only an explicit existing `RustDesk.exe`, rejects a reparse-point file, records a SHA-256 approval fingerprint, requires cached Windows Authenticode trust, and revalidates the bytes and signature at launch. Sharing uses no arguments; control passes only `--connect` and one constrained peer ID through `ProcessStartInfo.ArgumentList` with `UseShellExecute=false`. RustDesk owns transport, authentication, consent, elevation behavior, updates, and session termination. Soltex does not provide passwords, unattended access, elevation flags, service installation, or a hidden session.

## Performance isolation

- No periodic whole-disk scan.
- One aggregate protection query per minute, plus debounced refreshes after WSC change signals. Failed observations retry from five seconds up to a five-minute ceiling.
- Defender Operational history is queried only at Security-page startup/manual refresh, with a seven-day/24-event UI bound, ten-second timeout, and 512 KiB captured-output ceiling; excess bytes are drained and discarded before the result is rejected.
- One `FileSystemWatcher`, limited to `Imports` beneath the selected product root (`%LOCALAPPDATA%\Soltex` for a fresh profile, or the sole existing compatible root).
- Bounded queue: 128 paths, oldest entry dropped under overload.
- Debounce window: 750 ms; one sequential assessment worker.
- AMSI in-memory limit: 16 MiB; larger content is delegated to a Defender custom scan.
- Audit rotation: 4 MiB plus one prior segment.
- Security operations run asynchronously and are not shared with future audio/capture threads.
- System telemetry uses one 300 ms sampling window followed by a two-second delay. Failures retry sequentially with a bounded delay up to ten seconds; no overlapping sampler, whole-disk walk, executable-path query, GPU poller, or network poller is created.
- Performance sampling is active only while Home or Performance is visible in a non-minimized window. It is cancelled while hidden, minimized, or closing. Completed navigation animations release their clocks, and any remaining workspace animation is cleared before minimize.
- Service inventory is captured only at startup, Applications navigation, or explicit refresh. It has no polling loop and exposes no mutation command.
- Audio endpoint/session discovery runs only at startup, Audio navigation, manual refresh, or after an explicit session request. It creates no resident audio thread, installs no virtual device, observes at most 128 session slots, and performs no write without a direct user request. The default automated suite is read-only; the opt-in owner-host write check uses only a task-owned silent session and writes its observed values back unchanged. Optional fallback reminders persist only direction-scoped endpoint fingerprints. System-default assignment remains in the user-owned Windows Sound page opened through one fixed `ms-settings:sound` launch plan.
- Remote Assist has no polling loop or resident network component. File hashing and Authenticode checks occur only when a client is selected or a user confirms a launch.
- Update planning has no scheduler or resident downloader. It runs only from explicit input, applies declared byte/time/count ceilings, holds inert artifacts in private staging, and deletes them when the prepared preview is disposed.

## Future subsystem boundaries

- Per-session volume/mute is implemented through supported Core Audio shared-session interfaces. Default-device observation plus a user-mediated Windows Settings handoff is implemented without binding undocumented policy interfaces. Direct system endpoint switching, routing, processing, and stable system-wide virtual endpoints remain separate work requiring documented APIs and, where necessary, a separately designed/signed SysVAD/APO-derived package or other supported architecture.
- Clips requires Windows Graphics Capture, D3D11, Media Foundation hardware encoding, a bounded segment ring, and a separate click-through/no-activate overlay.
- Device Fabric Stage 1 now has a local observation and UI, but a future multi-device fabric still requires capability-bounded Windows/macOS agents, a signed and replay-resistant job protocol, device-local approval policy, OS-backed secret storage, and connector isolation. Tailscale, RustDesk, the NAS, Google Drive, and Box remain separate trust domains. See `docs/PERSONAL_DEVICE_FABRIC.md`.
- A true antivirus provider is a separate product program, not an extension of the current WPF process.
