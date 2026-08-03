# Soltex

Soltex is a proprietary personal-computing workspace. **Soltex** is the canonical product and repository name; the current Windows solution, assemblies, namespaces, and selected filenames retain the legacy `WaveSlate.*` identifier until a separately scoped compatibility migration is approved.

The current desktop solution covers four related product areas:

- **Audio** — future per-application routing, parametric EQ, microphone processing, and stream mixes.
- **Clips** — future bounded rolling capture with a separate, non-injected overlay.
- **Security** — the implemented focus: a lightweight companion that cooperates with the antivirus provider registered with Windows, plus bounded local supply-chain verification primitives.
- **Remote Assist** — a consent-first launcher for a separately installed, Windows-trusted RustDesk client; Soltex does not own or embed the remote-session transport.

This is a clean-room product. It is not affiliated with, endorsed by, or derived from SteelSeries, Malwarebytes, Zen Browser, AppControl, NZXT, or RustDesk. It contains no copied binaries, signatures, detection models, private protocols, branding, or UI assets from those products. RustDesk remains a separately licensed external program.

## What works now

The .NET 10 WPF solution builds and executes on Windows. The Security companion currently provides:

- provider-neutral antivirus health through Windows Security Center;
- Windows Security Center change notification with bounded polling fallback;
- detailed Microsoft Defender status when Defender is available;
- Defender operating-mode visibility and a bounded, path-redacted Operational event timeline;
- supported Defender quick, file, and folder scan requests;
- security-intelligence update requests;
- AMSI inspection before Soltex consumes bounded untrusted content;
- SHA-256 assessment and exact-hash allow decisions;
- Authenticode verification through `WinVerifyTrust`;
- authenticated, recoverable quarantine with restore and delete;
- a bounded watcher limited to Soltex's import folder;
- privacy-preserving, HMAC-chained local audit events.

The current supply-chain foundation adds:

- an explicit publisher policy that combines Windows Authenticode trust, a single requested embedded signature, zero secondary signatures, code-signing EKU, exact subject-name SHA-256, and SubjectPublicKeyInfo SHA-256;
- strict schema-2 `Soltex` release manifests signed with detached RSA-PSS/SHA-256 signatures;
- canonical Windows-relative manifest paths, explicit UTC publication time, length and SHA-256 validation, and reparse-point rejection;
- authenticated per-user release-sequence state that rejects lower sequences and same-sequence/different-manifest equivocation;
- bounded ZIP staging that rejects traversal, Windows device names, alternate-data-stream syntax, case collisions, reparse/symbolic links, unsupported entry types, excessive entry counts, expanded sizes, and compression ratios;
- private staging roots with failure, cancellation, and owner-disposal cleanup.

These are primitives, not an installer or updater. No production Soltex signing identity is pinned in source, staged archive content is not executed or installed, and no release transaction currently composes download, staging, manifest verification, publisher authorization, installation, rollback, or recovery.

The current Remote Assist source provides explicit RustDesk executable selection, Authenticode and SHA-256 revalidation, fixed shell-free launch arguments, constrained peer IDs, local confirmation, and peer-ID-free auditing. It does not bundle RustDesk, store passwords, enable unattended access, request elevation, install a service, or hide the external client.

System-wide real-time scanning, behavior monitoring, cloud intelligence, and remediation come from Microsoft Defender or another antivirus provider registered with Windows. Soltex does not disable or replace that provider.

## Build and verify

Requirements:

- Windows 10 version 2004 or newer, or Windows 11;
- .NET 10 SDK;
- PowerShell 5.1 and the Defender module for Defender-specific controls.

```powershell
dotnet build .\WaveSlate.sln --configuration Release

dotnet run `
  --project .\tests\WaveSlate.Security.Tests\WaveSlate.Security.Tests.csproj `
  --configuration Release `
  --no-build

dotnet run `
  --project .\tests\WaveSlate.Security.SupplyChain.Tests\WaveSlate.Security.SupplyChain.Tests.csproj `
  --configuration Release `
  --no-build
```

The safe EICAR interoperability check is opt-in and submits the harmless marker to AMSI in memory only:

```powershell
.\eng\verify.ps1 -RunEicar
```

Run the desktop app:

```powershell
dotnet run --project .\src\WaveSlate.App\WaveSlate.App.csproj --configuration Release
```

## Evidence

The implementation code at branch commit `592b96d1a31779676d797ad3c1033b5ccd63975e` was exercised by GitHub Actions run `30857239356` on Windows Server 2025 with .NET SDK 10.0.302:

- Release build: **passed**, 0 warnings and 0 errors;
- existing focused suite: **27/27 passed**;
- supply-chain suite: **17/17 passed**;
- opt-in hosted EICAR suite: **27/28** because the installed hosted-runner AMSI provider returned native result `1` for the in-memory marker;
- native Security render: **passed**;
- native Remote Assist render: **passed**;
- render artifact verification: **passed**, with no render-error files.

The retained Actions artifact is `soltex-windows-evidence-30857239356-1` (artifact ID `8872960871`, ZIP SHA-256 `55E3369054F86740E3DB5B8C2C1440A28883E70B2D9F509D0F7E692C924B0946`). The hosted server did not expose a usable live `wscapi.dll` provider boundary, so this run proves bounded fallback behavior rather than successful live provider enumeration. Native rendering is runtime evidence, not owner visual acceptance.

Documentation:

- Current status and nonclaims: [`docs/IMPLEMENTATION_STATUS.md`](docs/IMPLEMENTATION_STATUS.md).
- Exact commands and evidence ledger: [`docs/VALIDATION.md`](docs/VALIDATION.md).
- Publisher, signed-release, anti-rollback, and archive boundaries: [`docs/SUPPLY_CHAIN_SECURITY.md`](docs/SUPPLY_CHAIN_SECURITY.md).
- Security architecture: [`docs/SECURITY_ENGINEERING_HANDOFF.md`](docs/SECURITY_ENGINEERING_HANDOFF.md).
- Threat model: [`docs/THREAT_MODEL.md`](docs/THREAT_MODEL.md).
- Remote Assist license and trust boundary: [`docs/REMOTE_ASSIST.md`](docs/REMOTE_ASSIST.md).
- Proposed private device fabric, NAS, Drive, and isolated Box boundaries: [`docs/PERSONAL_DEVICE_FABRIC.md`](docs/PERSONAL_DEVICE_FABRIC.md).
- Complete built/current/planned systems and visual/coding roadmap: [`docs/MASTER_PROJECT_BLUEPRINT.md`](docs/MASTER_PROJECT_BLUEPRINT.md).

## Important boundary

Soltex Security is not a registered third-party antivirus product. It has no production detection organization, cloud reputation service, Windows Security Center provider registration, file-system minifilter, protected anti-malware service, ELAM component, MVI participation, independent efficacy certification, or supported detection-rate claim.

The new supply-chain code also does not constitute a signed release system. Production certificate procurement and custody, release-key storage, explicit pin rotation, timestamping policy, signed installer selection, authenticated update transport, transactional activation, rollback/recovery, uninstall, and retained release evidence remain separate work.

Copyright © 2026. All rights reserved. See [`LICENSE.txt`](LICENSE.txt).
