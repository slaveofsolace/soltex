# Architecture

## Runtime shape

```text
WaveSlate.App (WPF, unelevated)
    |
    +-- Windows Security Center health (wscapi.dll)
    +-- Windows Security Center change signal (WscRegisterForChanges)
    +-- Defender status/scans/updates (fixed PowerShell commands)
    +-- Defender Operational event reader (fixed, bounded Get-WinEvent query)
    +-- WaveSlate.RemoteAssist
    |     +-- bounded RustDesk.exe discovery/selection
    |     +-- SHA-256 approval fingerprint
    |     +-- fixed external launch plans (no shell)
    |     +-- constrained peer-ID value object
    |           |
    |           +-- separately installed RustDesk UI/process
    +-- WaveSlate.Security
          +-- protection monitor with retry/backoff/recovery state
          +-- AMSI intake scanner
          +-- file hashing and assessment
          +-- Authenticode verifier
          +-- detached signed-manifest verifier
          +-- exact-hash allow list
          +-- authenticated quarantine
          +-- HMAC-chained local audit log
          +-- bounded WaveSlate Imports watcher
```

WaveSlate installs no Windows service, background tray process, kernel driver, browser extension, network proxy, or cloud backend in this release. A user-approved RustDesk process is external to WaveSlate and retains its own runtime behavior.

## Trust boundaries

- **Windows provider boundary:** `WindowsSecurityCenter` reads aggregate antivirus health. Explicit WSC `Good`, `Poor`, `Snoozed`, and `NotMonitored` states are authoritative. Defender detail flags are a fallback only when WSC is unavailable and Defender reports `Normal` with antivirus and real-time protection active. `PowerShellDefenderClient` launches the System32 Windows PowerShell host in its executable directory, imports Defender, Diagnostics, and Utility manifests through direct `$PSHOME` paths, module-qualifies every security cmdlet, invokes only fixed supported commands, and replaces raw error output with a bounded exit-code diagnostic before it reaches audit-facing models.
- **Native loading boundary:** the security assembly constrains its Windows P/Invoke resolution to System32. AMSI, WSC, WinTrust, Crypt32, and Kernel32 imports cannot be satisfied from the application or current directory.
- **Monitoring boundary:** `WindowsSecurityChangeMonitor` receives only a WSC change signal. `ProtectionMonitor` serializes refreshes, coalesces signals through a one-slot channel, uses one timeout-cancelled channel wait per cycle, polls at one-minute intervals, retries nonfatal failures with bounded exponential backoff, keeps the last successful observation, emits an explicit recovered state, and isolates subscriber failures so one UI observer cannot stop monitoring.
- **Event boundary:** the Defender Operational reader asks Windows for at most 24 recent allow-listed event IDs in the app and 100 at the library boundary, with a 512 KiB stdout byte ceiling enforced while the child stream is read. It treats only a no-matching-events condition as an empty result, surfaces other provider/access failures, and re-enforces the requested count after deserialization. `DefenderEventLogParser` constructs descriptions from an allow-list of fields and never exposes file, process, or scan-resource paths.
- **Content-intake boundary:** `FileAssessmentService` normalizes existing non-reparse files, hashes them, checks the exact-hash allow list, and sends at most 16 MiB to AMSI.
- **Release boundary:** `IntegrityManifestVerifier` verifies exact manifest bytes with RSA-PSS/SHA-256 before validating bounded relative paths, lengths, and hashes. `AuthenticodeVerifier` asks Windows to validate an individual signed file.
- **Local-state boundary:** DPAPI protects a per-user HMAC key. Allow-list and quarantine indexes are authenticated. Quarantined payloads are re-hashed before restoration.
- **Privacy boundary:** audit records store an HMAC of a full path rather than the raw path. The log is chained so modification is detectable, though a same-user attacker can still truncate or delete local state.
- **Remote-assistance boundary:** WaveSlate never embeds the AGPL RustDesk runtime. It accepts only an explicit existing `RustDesk.exe`, rejects a reparse-point file, records a SHA-256 approval fingerprint, requires cached Windows Authenticode trust, and revalidates the bytes and signature at launch. Sharing uses no arguments; control passes only `--connect` and one constrained peer ID through `ProcessStartInfo.ArgumentList` with `UseShellExecute=false`. RustDesk owns transport, authentication, consent, elevation behavior, updates, and session termination. WaveSlate does not provide passwords, unattended access, elevation flags, service installation, or a hidden session.

## Performance isolation

- No periodic whole-disk scan.
- One aggregate protection query per minute, plus debounced refreshes after WSC change signals. Failed observations retry from five seconds up to a five-minute ceiling.
- Defender Operational history is queried only at Security-page startup/manual refresh, with a seven-day/24-event UI bound, ten-second timeout, and 512 KiB captured-output ceiling; excess bytes are drained and discarded before the result is rejected.
- One `FileSystemWatcher`, limited to `%LOCALAPPDATA%\WaveSlate\Imports`.
- Bounded queue: 128 paths, oldest entry dropped under overload.
- Debounce window: 750 ms; one sequential assessment worker.
- AMSI in-memory limit: 16 MiB; larger content is delegated to a Defender custom scan.
- Audit rotation: 4 MiB plus one prior segment.
- Security operations run asynchronously and are not shared with future audio/capture threads.
- Remote Assist has no polling loop or resident network component. File hashing and Authenticode checks occur only when a client is selected or a user confirms a launch.

## Future subsystem boundaries

- Audio requires WASAPI/MMDevice work and, for stable system-wide virtual endpoints, a signed SysVAD/APO-derived package or another documented routing architecture.
- Clips requires Windows Graphics Capture, D3D11, Media Foundation hardware encoding, a bounded segment ring, and a separate click-through/no-activate overlay.
- The proposed personal device fabric requires capability-bounded Windows/macOS agents, a signed and replay-resistant job protocol, device-local approval policy, OS-backed secret storage, and connector isolation. Tailscale, RustDesk, the NAS, Google Drive, and Box remain separate trust domains. See `docs/PERSONAL_DEVICE_FABRIC.md`.
- A true antivirus provider is a separate product program, not an extension of the current WPF process.
