# WaveSlate

WaveSlate is a proprietary Windows desktop scaffold for four related product areas:

- **Audio** — future per-application routing, parametric EQ, microphone processing, and stream mixes.
- **Clips** — future bounded rolling capture with a separate, non-injected overlay.
- **Security** — the implemented focus of this repository: a lightweight companion that cooperates with the antivirus provider registered with Windows.
- **Remote Assist** — a consent-first launcher for a separately installed, Windows-trusted RustDesk client; WaveSlate does not own or embed the remote-session transport.

This is a clean-room product. It is not affiliated with, endorsed by, or derived from SteelSeries, Malwarebytes, Zen Browser, or RustDesk. It contains no copied binaries, signatures, detection models, private protocols, branding, or UI assets from those products. RustDesk remains a separately licensed external program.

## What works now

The last executed .NET 10 WPF baseline builds and runs on Windows. Current source adds unexecuted security hardening and Remote Assist changes. Its Security page provides:

- provider-neutral antivirus health through Windows Security Center;
- Windows Security Center change notifications with one-minute polling fallback;
- detailed Microsoft Defender status when Defender is active;
- Defender active/passive-mode visibility and a bounded, path-redacted Operational event timeline;
- supported Defender quick, file, and folder scan orchestration;
- security-intelligence update orchestration;
- AMSI inspection before WaveSlate consumes bounded untrusted content;
- SHA-256 assessment and exact-hash allow-listing;
- detached RSA-PSS signed-manifest verification;
- Authenticode verification through `WinVerifyTrust`;
- authenticated, recoverable quarantine with restore and delete;
- a bounded watcher limited to WaveSlate's import folder;
- privacy-preserving, HMAC-chained local audit events.

The current Remote Assist source adds explicit RustDesk executable selection, Authenticode and SHA-256 revalidation, fixed shell-free launch arguments, constrained peer IDs, local confirmation, and peer-ID-free auditing. It does not bundle RustDesk, store passwords, enable unattended access, request elevation, install a service, or hide the external client.

System-wide real-time scanning, behavior monitoring, cloud intelligence, and remediation come from Microsoft Defender or another antivirus provider registered with Windows. WaveSlate does not disable or replace that provider.

## Build and verify

Requirements:

- Windows 10 version 2004 or newer, or Windows 11;
- .NET 10 SDK;
- PowerShell 5.1 and the Defender module for Defender-specific controls.

```powershell
dotnet build .\WaveSlate.sln --configuration Release
dotnet run --project .\tests\WaveSlate.Security.Tests\WaveSlate.Security.Tests.csproj --configuration Release --no-build
```

The safe EICAR integration test is opt-in and scans the harmless test marker in memory only:

```powershell
.\eng\verify.ps1 -RunEicar
```

Run the desktop app:

```powershell
dotnet run --project .\src\WaveSlate.App\WaveSlate.App.csproj --configuration Release
```

## Evidence

- Last executed Release build: zero warnings and zero errors on .NET SDK 10.0.302, before the five latest source-only security corrections.
- Last executed focused suite: 16/16 tests passed, including live WSC health/change registration, simulated monitor outage/recovery, shutdown-race regression, redacted Defender event correlation, Authenticode, and benign AMSI. Current source defines 27 tests after eight security-focused regression additions and three deterministic Remote Assist boundary tests; none of the eleven additions has run against the current source because the Codex PowerShell command path timed out during startup. The in-memory EICAR integration passed on the prior baseline, and the 28-check expanded opt-in suite remains pending until command startup responds or the supplied manual validation command returns evidence.
- Measured observation cost on the validation machine: WSC callback registration 5.4 ms, full Defender health 607.4 ms, 16-event query 422.0 ms, mean WSC read 0.336 ms, and mean 4 KiB AMSI call 1.033 ms.
- Last native WPF render, before the Zen-inspired visual and Remote Assist source changes: [`artifacts/visual/security-monitoring-v1.png`](artifacts/visual/security-monitoring-v1.png).
- Current status and nonclaims: [`docs/IMPLEMENTATION_STATUS.md`](docs/IMPLEMENTATION_STATUS.md).
- Security architecture: [`docs/SECURITY_ENGINEERING_HANDOFF.md`](docs/SECURITY_ENGINEERING_HANDOFF.md).
- Threat model: [`docs/THREAT_MODEL.md`](docs/THREAT_MODEL.md).
- Remote Assist license and trust boundary: [`docs/REMOTE_ASSIST.md`](docs/REMOTE_ASSIST.md).
- Proposed private device fabric, NAS, Drive, and isolated Box boundaries: [`docs/PERSONAL_DEVICE_FABRIC.md`](docs/PERSONAL_DEVICE_FABRIC.md).
- Complete built/current/planned systems and visual/coding roadmap: [`docs/MASTER_PROJECT_BLUEPRINT.md`](docs/MASTER_PROJECT_BLUEPRINT.md).
- Copy-ready Pro-chat GitHub scaffold prompt: [`WAVESLATE_PRO_CHAT_MASTER_BLUEPRINT.txt`](WAVESLATE_PRO_CHAT_MASTER_BLUEPRINT.txt).
- Copy-ready next-stage device-fabric goal: [`WaveSlate_DEVICE_FABRIC_GOAL_PROMPT.txt`](WaveSlate_DEVICE_FABRIC_GOAL_PROMPT.txt).
- Copy-ready continuation: [`WaveSlate_HANDOFF_PROMPT.txt`](WaveSlate_HANDOFF_PROMPT.txt).

## Important boundary

WaveSlate Security is not currently a registered third-party antivirus product. A true replacement antivirus requires, among other things, a production detection organization, secure cloud intelligence, independently measured efficacy, signed update infrastructure, Windows Security Center provider integration, and potentially a signed minifilter, protected service, and ELAM program participation. Those capabilities are not represented as implemented.

Copyright © 2026. All rights reserved. See [`LICENSE.txt`](LICENSE.txt).
