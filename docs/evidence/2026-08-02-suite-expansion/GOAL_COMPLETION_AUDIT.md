# WaveSlate security-goal completion audit

Audit timestamp: 2026-08-02 after the user explicitly revoked the temporary PC-manager hold; updated through 2026-08-03 source and publication-handoff work.

Objective audited: finish a lightweight, defensible antivirus/security-monitoring slice using supported Windows interfaces; preserve work; separate Defender/Malwarebytes interoperability lessons; add focused tests, failure/recovery behavior, performance measurements, and factual proportional documentation; do not weaken Windows security or claim unsupported antivirus capability.

## Requirement-by-requirement evidence

| Requirement | Current evidence | Finding |
| --- | --- | --- |
| Re-verify tree and ownership | `PC_STABILITY_CHECKPOINT.md`; live `main` / `bf2662de80992cfed761625642f084d3caaa0f04` status; all dirty paths enumerated as task-owned | Proven current; no unrelated work overwritten, staged, stashed, cleaned, or reset |
| Lightweight supported Windows architecture | `WindowsSecurityCenter.cs`, `WindowsSecurityChangeMonitor.cs`, `PowerShellDefenderClient.cs`, `AmsiContentScanner.cs`, `ProtectionMonitor.cs` | Implemented with WSC health/change APIs, fixed Defender cmdlets, bounded Defender Event Log reads, and AMSI |
| Provider-neutral coexistence | `DefenderHealthSnapshot.ObservationSucceeded`, `AMRunningMode`, provider-managed monitor state, WSC aggregate health | Explicit WSC states now take precedence; Defender details are only a disclosed fallback for unknown WSC health when Defender is `Normal` and active. A focused precedence regression is pending execution |
| Bounded event monitoring and privacy | 24-event UI limit, 100-event library ceiling, seven-day UI lookback, ten-second timeout, 512 KiB byte ceiling, allow-listed event IDs, post-deserialization count enforcement, `DefenderEventLogParser` path redaction | Parser/live query passed before the advisory; current source adds true byte-bounded reads, distinguishes no-match from provider/access failure, re-enforces the requested count, and includes pending regressions |
| Failure and recovery behavior | Serialized refresh gate, five-second retry with bounded exponential backoff, last-known-good snapshot, explicit recovered state, WSC polling fallback, callback unregistration, provider/subscriber exception isolation | Earlier simulated outage/recovery and pending-wake shutdown regressions passed; later fault-isolation regression is pending execution |
| Focused tests | `tests/WaveSlate.Security.Tests/Program.cs`; 16/16 passed before the advisory; current source adds eight security-focused regressions and three Remote Assist boundary regressions | The last run predates all eleven additions; 27/27 current-source proof remains pending command-startup recovery or manual result ingestion |
| Performance measurements | WSC callback registration 5.4 ms; Defender health 607.4 ms; 16-event query 422.0 ms; mean WSC read 0.336 ms; mean 4 KiB AMSI call 1.033 ms | Measured before the advisory and documented; not detection-efficacy evidence |
| Native runtime presentation | `artifacts/visual/security-monitoring-v1.png`, SHA-256 `1194B76817178DC65BC6DD74A35C31E96C598F036DEDD589278C5F2D2C785F9C` | Render-smoke passed and artifact was visually inspected after fixing a real shutdown race |
| Defender/Malwarebytes separation | `docs/RESEARCH_AUDIT.md`, `docs/SECURITY_ENGINEERING_HANDOFF.md` and first-party links | Documented as interoperability lessons only; no code, signatures, models, driver design, protocol, endpoint, or vendor asset acquired |
| No security weakening | Static search across `src`, `tests`, and `eng` found no `Set/Add/Remove-MpPreference`, real-time disabling, service modification, exclusion, minifilter, or ELAM implementation | Proven by current-source inspection; UI only opens Windows-owned security surfaces on explicit user action |
| No new risky dependency or native/module preload path | No `PackageReference` or extra `FrameworkReference` exists in `src` or `tests`; `NativeImportPolicy.cs` constrains security P/Invokes to System32; fixed PowerShell scripts import direct system manifests and module-qualify security cmdlets | Proven by current project/source files; two focused reflection regressions pending execution |
| Proportional claims | README, implementation status, threat model, validation, capability matrix, and UI scope disclosure | Explicitly denies registered-provider, minifilter, ELAM/PPL, EDR, cloud-reputation, efficacy, certification, and production-readiness claims |
| Remote Assist license/trust separation | `src/WaveSlate.RemoteAssist`, `docs/REMOTE_ASSIST.md`, external RustDesk 1.4.7 reference cache/hash | Current source uses a separate-process, fixed-argument adapter; no AGPL source/binary, password, unattended access, elevation, service command, or hidden session is embedded in WaveSlate |
| Remote Assist launch integrity | Exact filename/reparse checks, SHA-256 approval fingerprint, launch-time byte revalidation, WPF Authenticode recheck, constrained peer ID, `UseShellExecute=false` | Statically present with three deterministic tests; build/runtime proof remains open |

## Open acceptance evidence

The temporary PC-manager hold has been revoked by the user. The objective remains unproven because a bounded no-op PowerShell command still times out during startup, preventing current-source compilation and runtime verification. The following exact evidence is required when command startup responds:

1. Re-run the Release build from the dirty source and confirm zero warnings/errors.
2. Re-run `eng\verify.ps1 -RunEicar` and confirm 28/28 checks: 27 current default tests plus the opt-in in-memory EICAR interoperability test.
3. Re-run native render-smoke for both the default Security page and `--panel remote`, inspect both artifacts, and confirm the shutdown path stays clean and the Remote Assist layout/states render correctly.
4. Update `docs/VALIDATION.md`, `docs/IMPLEMENTATION_STATUS.md`, the reasoning ledger, and this audit with current-source results.
5. GitHub publication is now explicitly owner-requested, but the commit must preserve these verification nonclaims rather than imply the current source passed.

Until those checks run through a responsive command path, all source, test, documentation, and earlier runtime evidence is durable but final completion remains unproven.

## Lightweight continuation after the advisory

Static inspection found that a poll timeout could leave an earlier channel read pending. Repeated timeouts could therefore let an abandoned reader consume a later WSC/manual refresh signal. `ProtectionMonitor` now uses one linked, timeout-cancelled `WaitToReadAsync` per cycle, and the focused suite contains a regression that waits through multiple polling cycles before requiring a prompt manual refresh.

The same inspection found that the Defender runner's stated output ceiling was only applied after `ReadToEndAsync`, leaving peak memory unbounded. It now captures at most the configured UTF-8 byte limit, drains and discards excess, rejects overflow explicitly, and observes I/O tasks on timeout/cancellation. An end-to-end oversized-child regression covers that boundary.

Provider-coexistence review then found that active Defender detail flags could override an explicit WSC warning state. `IsProtected` now makes explicit WSC states authoritative and permits only a disclosed `Normal`/active Defender fallback when WSC is unavailable.

Native-boundary review found ten system DLL imports without an explicit search-path policy. `NativeImportPolicy.cs` now constrains the security assembly to System32 and a reflection regression covers the policy. PowerShell-boundary review then found that relying on command-name module auto-loading left module provenance implicit. Fixed scripts now import the system Defender, Diagnostics, and Utility manifests by direct `$PSHOME` paths and module-qualify each security cmdlet; a second reflection regression covers those invariants.

The 2026-08-03 continuation adds a clean external RustDesk boundary and Zen-inspired Remote Assist page. RustDesk source was cached only outside the repository because its AGPL-3.0 obligations do not permit treating it as proprietary scaffolding. The adapter fingerprints a selected client, rechecks bytes/signature, constrains peer IDs, uses fixed shell-free arguments, and keeps transport/authentication/consent/session ownership external. Three focused Remote Assist regressions were added.

A later bounded security review added provider/subscriber fault isolation, an anchored PowerShell working directory, audit-facing stderr redaction, explicit no-match versus provider/access event-query behavior, and post-deserialization event-count enforcement. Three focused regressions bring current source to 27 default tests. These source changes were made without a fresh compiler/runtime path, so the Release build, 27-test default gate, 28-check EICAR gate, and current native renders remain required.
