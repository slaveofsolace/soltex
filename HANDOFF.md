# Soltex handoff

Snapshot: 2026-08-05 (America/Chicago)

## Repository state

```text
repository: slaveofsolace/soltex
base branch: main
base commit: 8a7b4a76ec4fd262216d78a055d72ff48f1c3da2
feature branch: audit/soltex-v1-quality-pass
current head: a2ac7718fd207364c9a74199585a9720ae53821f
pull request: https://github.com/slaveofsolace/soltex/pull/7
PR state: draft, open, not merged
```

No rebase, force push, merge, service, driver, privilege expansion, Defender mutation, antivirus exclusion, generic shell, or proprietary security reverse engineering was used.

## Commits

```text
c60174a736febdad2b50cb96963471555208ca64  feat: add bounded live network telemetry
229b3acbcfc9c06b6394d16de120699cbea81fcb  perf: suspend telemetry outside live workspaces
a2ac7718fd207364c9a74199585a9720ae53821f  ci: add portable package smoke evidence
```

## Implemented

### Network telemetry

The existing Monitoring sample now observes active Windows network-interface counters before and after the same bounded sample window already used for CPU/process deltas.

It provides:

- receive, send, and total bytes per second;
- bounded per-interface rows;
- control-character and length sanitization;
- aggregate Home and Monitoring sparklines;
- explicit unavailable/partial states;
- no packet contents, destinations, connection history, extra timer, service, package, or privilege.

Non-routable/filter-only interfaces are excluded by requiring an active non-loopback unicast IPv4/IPv6 address.

### Telemetry lifecycle

The existing sampling loop now runs only when:

- the window is loaded;
- the window is not closing;
- the window is not minimized;
- Home or Monitoring is visible.

Transitions are coalesced through the dispatcher and serialized. Hidden workspaces and minimized windows cancel and dispose the loop.

### Package smoke

A second workflow now:

- publishes the self-contained single-file `win-x64` executable;
- launches the published artifact;
- produces a native Home render;
- records SHA-256 and Authenticode status;
- uploads the executable and evidence.

The current package is explicitly recorded as unsigned.

## Files changed

```text
.github/workflows/package-smoke.yml
src/Soltex.App/Controls/Sparkline.cs
src/Soltex.App/MainWindow.TelemetryLifecycle.cs
src/Soltex.App/TelemetryActivityPolicy.cs
src/Soltex.App/Views/HomeView.xaml
src/Soltex.App/Views/HomeView.xaml.cs
src/Soltex.App/Views/MonitoringView.xaml
src/Soltex.App/Views/MonitoringView.xaml.cs
src/Soltex.App/Views/TelemetryDisplay.cs
src/Soltex.Monitoring/BoundedTelemetryHistory.cs
src/Soltex.Monitoring/SystemTelemetryProvider.cs
src/Soltex.Monitoring/TelemetryMath.cs
src/Soltex.Monitoring/TelemetryModels.cs
tests/Soltex.App.Tests/Program.cs
tests/Soltex.Monitoring.Tests/Program.cs
docs/AUDIT.md
docs/IMPLEMENTATION_STATUS.md
docs/INDEX.md
docs/VALIDATION.md
HANDOFF.md
```

## Final evidence

Source gate:

```text
run: 31065075683
job: 92500998931
artifact: soltex-windows-evidence-31065075683-1
artifact ID: 8953553034
digest: sha256:850946fd3fc57e7a318565ca26428627cb3caa6a822409fd2112f5a14af9d4ff
```

Results:

```text
build: 0 warnings, 0 errors
Security/Remote: 31/31
Supply chain: 18/18
Hardening: 12/12
Update: 17/17
Device Fabric: 24/24
Monitoring: 15/15
WPF application: 7/7
hosted EICAR/AMSI: 31/32
native renders: Home, Monitoring, Devices, Security, Remote, Updates passed
```

Package gate:

```text
run: 31065075715
job: 92498644583
artifact: soltex-package-smoke-31065075715-1
artifact ID: 8953553036
digest: sha256:c48026fc3b2bb011b42563c2cd7b32eb5795bb9dbf111d7bc4324d0c14523088
```

Published executable:

```text
file: Soltex.exe
length: 71485765 bytes
sha256: E3AE863B30A16EB091EF8C73EB6985EEC9C15760EBC5114F775FB44333A5F2F5
Authenticode: NotSigned
launch/render: passed
```

The repository owner-host record separately reports 32/32 EICAR. Do not replace the hosted 31/32 result with it.

## Highest-priority remaining work

1. **Trusted distribution:** select the publisher identity, protect the signing key, sign and timestamp executable/installer, verify exact hashes/signatures, and test install/repair/uninstall.
2. **Accessibility:** complete keyboard/UI Automation, high-contrast, reduced-motion, viewport/scaling, and owner visual checks.
3. **Resource evidence:** 30-minute working-set/CPU soak, minimize/restore, suspend/resume, and repeated navigation.
4. **Next lightweight feature:** read-only installed-application/startup inventory through documented registry/startup locations. Do not use `Win32_Product`, silently uninstall, or add a cleanup/optimizer action.
5. **Audio foundation:** supported Core Audio endpoint/session enumeration; no live EQ/routing claim before a tested processing path.
6. **Release automation:** only after signing and installer evidence, create immutable GitHub Release automation and provenance/SBOM evidence.

## Security and product nonclaims

Soltex is not:

- a registered antivirus provider;
- a substitute for Defender or another Windows-registered provider;
- integrated with proprietary Malwarebytes internals;
- a production update service;
- signed for trusted public distribution;
- an unattended remote-management agent;
- a generic remote shell;
- production-ready;
- accessibility- or visually-approved.

## Exact resume step

Review PR #7 and its two green workflow runs. After owner review, keep the next branch focused on **signed distribution** or **read-only application/startup inventory**—not both. Begin by re-running the complete source gate at the PR head and verifying the package artifact's recorded `NotSigned` status before selecting the signing or inventory design.
