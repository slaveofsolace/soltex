# Soltex V1 audit

Snapshot: 2026-08-05 (America/Chicago)  
Repository: `slaveofsolace/soltex`  
Audited base: `8a7b4a76ec4fd262216d78a055d72ff48f1c3da2`  
Audit branch: `audit/soltex-v1-quality-pass`  
Branch head: `a2ac7718fd207364c9a74199585a9720ae53821f`  
Pull request: [#7](https://github.com/slaveofsolace/soltex/pull/7)

## Executive summary

Soltex has moved beyond a UI scaffold. The current Windows solution has distinct Security, Update, Remote Assist, Monitoring, Device Fabric, and WPF application projects; a shared dark/coral control theme; an evidence-oriented test suite; a self-contained per-user packaging path; and explicit capability boundaries.

The codebase is healthy enough to continue incrementally. It is not ready for trusted public distribution. The largest immediate release risk is that the published executable and installer are unsigned. The largest engineering risks are the amount of orchestration retained in the WPF shell, incomplete accessibility/scaling evidence, and the gap between the broad product roadmap and the relatively small set of live capabilities.

This audit deliberately avoided a framework rewrite. Three narrow changes were implemented:

1. live read-only network throughput was added to the existing bounded telemetry sample;
2. telemetry now stops when its live pages are hidden or the window is minimized;
3. a separate package-smoke workflow now publishes and launches the self-contained executable and records its exact hash and Authenticode state.

No package dependency, service, driver, listener, elevation path, Defender setting change, antivirus exclusion, generic shell, or proprietary security reverse engineering was introduced.

## Current evidence

Two workflows exercised the final branch head:

| Gate | Result |
|---|---|
| Release build | Passed, 0 warnings and 0 errors |
| Security/Remote focused suite | 31/31 |
| Supply-chain suite | 18/18 |
| Security hardening suite | 12/12 |
| Update-planner suite | 17/17 |
| Device Fabric suite | 24/24 |
| Monitoring suite | 15/15 |
| WPF application suite | 7/7 |
| Hosted EICAR/AMSI lane | 31/32; hosted AMSI returned native result `1` |
| Native renders | Home, Monitoring, Devices, Security, Remote Assist, Updates passed |
| Self-contained publish | Passed |
| Published executable launch/render | Passed |
| Published executable Authenticode | `NotSigned` |

The owner-controlled Windows evidence in the repository separately reports a 32/32 EICAR result. That does not change the hosted-runner result; they are different provider environments.

## Architecture and code quality

### Strong points

- Capability projects have clearer boundaries than a single privileged desktop monolith.
- The ordinary WPF application remains unelevated.
- Security and release operations use typed models, explicit limits, cancellation, and negative-path tests.
- The repository uses analyzers and warnings-as-errors.
- GitHub Actions are pinned to immutable commit SHAs, checkout does not persist credentials, and workflow permissions are read-only.
- No NuGet runtime dependency is required by the current solution.
- The existing design system includes reusable colors, typography, cards, buttons, progress controls, sliders, and focus states.
- Update planning stops at an inert preview. It does not install or activate content.
- RustDesk remains a separate visible application rather than linked proprietary code.

### Risks and debt

- `MainWindow` still coordinates many unrelated workspaces and lifecycle concerns. It should be decomposed gradually around existing interfaces; a wholesale MVVM/framework rewrite would add more cost than value.
- Security, Remote Assist, and Updates still contain large WPF surfaces that will become difficult to maintain if more logic is added directly.
- The main Windows workflow is intentionally comprehensive but sequential. A separate package lane now reduces one source of coupling; further splitting should be justified by measured duration and failure isolation.
- Several roadmap areas remain page shells or architecture documents rather than working features.
- Historical evidence is extensive and occasionally repeats the same nonclaims. Current status, validation, and handoff documents should stay canonical; old evidence belongs in Git history or retained artifacts.

## Security, privacy, and reliability

### Supported boundaries retained

- Windows Security Center remains the provider-neutral antivirus-health boundary.
- Defender details and supported operations are used only when Defender is available.
- AMSI protects content Soltex itself ingests; it is not represented as a Soltex detection engine.
- Authenticode, signed manifests, anti-rollback state, bounded archive staging, pinned HTTPS, and update-preview composition remain separate trust checks.
- RustDesk passwords, unattended access, hidden sessions, service installation, and elevation flags are not added.
- Process telemetry omits executable paths.
- Network telemetry exposes bounded interface names and rates, not packet payloads, destinations, or connection histories.
- Work remains local unless an explicit future connector is implemented.

### Material risks

1. **Unsigned distribution.** The branch package-smoke run records `Soltex.exe` as `NotSigned`. Until a production publisher identity and timestamping process exist, Windows reputation and publisher trust cannot be represented as established.
2. **No production update trust root.** The planner is implemented, but no production metadata key, release key, TLS pin set, or signed descriptor source is configured.
3. **Same-user compromise.** DPAPI/HMAC state and local journals protect ordinary integrity, not a fully compromised user account with control over the profile.
4. **Provider-dependent security evidence.** Hosted WSC and AMSI behavior differs from an owner-controlled desktop. Hosted fallback tests are not live-provider certification.
5. **No direct Malwarebytes integration.** Soltex cooperates through supported Windows provider boundaries. Public Malwarebytes product concepts may inform UX, but proprietary signatures, models, drivers, protocols, and internal behavior remain out of scope.

The request to weaken or evade operating-system protection was not implemented. A security feature is not improved by bypassing the platform controls it relies upon.

## Performance and maintainability

The current monitoring design is intentionally modest:

- one bounded sample window supplies CPU, process, and network deltas;
- network telemetry adds no new timer, thread, process, resident service, or package;
- histories are fixed-size ring buffers;
- process and interface result counts are bounded;
- process tables use WPF virtualization;
- telemetry now runs only while Home or Monitoring is visible and the window is not minimized;
- unavailable providers remain unavailable rather than producing synthetic values.

The CI measurements are diagnostic, not guarantees. A 30-minute working-set/CPU soak, suspend/resume cycle, repeated navigation test, and multi-monitor DPI test remain outstanding.

## UX and accessibility

The application has a coherent visual direction and a substantial shared theme. The Home, Monitoring, and Devices pages are no longer decorative-only surfaces, and the new network graphs use the same warm dark/coral/green language.

Remaining work:

- validate 1366×768, 1440p, 4K, and Windows scaling at 100%, 125%, 150%, and 200%;
- audit every interactive control for an accessible name, keyboard behavior, focus restoration, and non-color status;
- add explicit high-contrast resources rather than assuming the default palette remains legible;
- respect the Windows client-area-animation setting for all future motion;
- add text summaries and keyboard inspection for complex charts;
- test reduced motion and high contrast on an owner-controlled machine;
- keep animations functional and short; do not animate background decoration or security-critical actions.

## CI, packaging, and release

The source gate is strong and truthfully retains provider-specific failures. The new package workflow adds an independent check for the artifact users would actually run.

Still missing:

- a signed installer/package lane;
- timestamp and certificate-chain evidence;
- deterministic installer install/repair/uninstall smoke tests;
- an automated GitHub Release workflow;
- software-bill-of-materials and provenance attestation for a release candidate;
- upgrade/downgrade and retained-state tests across actual installed versions.

The Inno Setup path is per-user and unelevated. The package-smoke workflow intentionally skips installer construction because the hosted image is not currently treated as a trusted signing/release environment.

## Product claims

| Area | Current claim |
|---|---|
| Security | Lightweight Windows security companion; not a registered antivirus provider |
| Defender | Supported health/status/scan orchestration when available |
| Malwarebytes | No proprietary integration; provider-neutral Windows cooperation only |
| Monitoring | Live CPU, memory, fixed-volume, bounded process, and active-interface network observations |
| GPU/thermals/fans | Unavailable until a separately supported provider is selected |
| Device Fabric | Typed local inventory and capability policy; no remote executor or listener |
| Remote Assist | Consent-first external RustDesk handoff |
| Updates | Authenticated non-installing planner; no activation |
| Packaging | Self-contained executable and per-user installer script; currently unsigned |
| Audio/clips/privacy/apps/connectors | Planned or partial foundations, not complete tools |

## Priority roadmap

### P0 — trusted distribution

- select the legal publisher identity;
- protect the code-signing key in hardware or a managed signing service;
- sign and timestamp the executable and installer;
- verify the signature and exact hashes in CI/release evidence;
- add installer install/repair/uninstall tests;
- publish an immutable release manifest and GitHub Release only after those gates pass.

### P1 — accessibility and resilience

- complete keyboard/AutomationProperties audit;
- add high-contrast and reduced-motion behavior;
- test viewport/scaling matrix;
- run long-duration resource and navigation soak tests;
- split only the WPF orchestration that is demonstrated to be difficult to test or maintain.

### P2 — next lightweight product feature

Implement read-only installed-application and startup inventory using documented registry/startup locations. Avoid `Win32_Product`, silent uninstall, registry cleanup, and mutation until inventory provenance, duplicate handling, cancellation, and explicit confirmation are tested.

### P3 — audio foundation

Enumerate Windows audio endpoints and active sessions through supported Core Audio interfaces. Add volume/mute only after endpoint/session identity and change-notification behavior are tested. Do not claim live EQ/routing before a real processing path exists.

### P4 — media and clips

Use Windows media-session controls for local playback integration. Keep capture work explicit, opt-in, bounded, non-injected, and separate from protected-content or anti-cheat behavior.

## Best-practice references

- Microsoft WPF control virtualization and performance guidance: <https://learn.microsoft.com/dotnet/desktop/wpf/advanced/optimizing-performance-controls>
- Microsoft WPF performance guidance: <https://learn.microsoft.com/dotnet/desktop/wpf/advanced/optimizing-wpf-application-performance>
- Microsoft UI Automation overview: <https://learn.microsoft.com/dotnet/framework/ui-automation/ui-automation-overview>
- Windows high-contrast guidance: <https://learn.microsoft.com/windows/apps/design/accessibility/high-contrast-themes>
- Windows `SystemParametersInfo` client-area animation setting: <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-systemparametersinfow>
- Microsoft code-signing guidance: <https://learn.microsoft.com/windows-hardware/drivers/dashboard/code-signing-reqs>
- GitHub secure use of third-party actions: <https://docs.github.com/actions/security-for-github-actions/security-guides/security-hardening-for-github-actions#using-third-party-actions>

## Audit conclusion

The appropriate strategy is incremental: strengthen the artifact users run, add real read-only capabilities through supported Windows APIs, and keep every background activity and history bounded. Soltex does not need a framework rewrite or a large dependency graph to become more capable.
