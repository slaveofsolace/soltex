# Validation and evidence

Snapshot: 2026-08-04
Repository: `slaveofsolace/soltex`  
Canonical product: Soltex  
Current solution identifier: `Soltex.sln`

## Evidence rules

- A successful build is not a runtime test.
- A passed focused test proves only its stated boundary.
- A hosted-runner fallback test is not evidence that the same live Windows provider was available.
- A generated PNG is not native WPF evidence; the render-smoke output below is produced by the WPF application itself.
- Successful native rendering is not owner visual acceptance or accessibility conformance.
- The optional EICAR check is provider interoperability evidence, not a Soltex detection-rate test.
- Local checkout state must be observed locally; it cannot be inferred from the private remote.

## Current Windows evidence

Implementation commit:

```text
2a0699b2ca77b30fa636279b1d5ecab603a8bde9
```

Stacked branch and pull request:

```text
feat/soltex-immersive-workspace-v1
https://github.com/slaveofsolace/soltex/pull/5
```

Stacked identity base and pull request:

```text
refactor/soltex-identity-and-repo-coherence
57607a35da14968c0d729795a857fd250b6566d9
https://github.com/slaveofsolace/soltex/pull/4
```

Environment:

- GitHub Actions run `30925606488`;
- job `92046999983`;
- GitHub-hosted Windows runner;
- checkout directory `D:\a\soltex\soltex`.

Retained evidence:

- artifact name: `soltex-windows-evidence-30925606488-1`;
- artifact ID: `8899014386`;
- uploaded artifact ZIP size: 626,093 bytes;
- GitHub-recorded artifact ZIP SHA-256: `71905016B3A67F8CE340D90C2E404DEBFB185C3DAE8A973F21B80F1B8A94515`;
- independently downloaded artifact ZIP SHA-256: `71905016B3A67F8CE340D90C2E404DEBFB185C3DAE8A973F21B80F1B8A94515`;
- configured retention: 30 days;
- uploaded files: build and seven focused-suite logs, EICAR log, gate classification, and native Home, Monitoring, Devices, Security, Remote Assist, and Updates PNGs.

## Commands exercised by the workflow

### Release build

```powershell
dotnet build .\Soltex.sln --configuration Release
```

Result:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

### Existing focused suite

```powershell
dotnet run `
  --project .\tests\Soltex.Security.Tests\Soltex.Security.Tests.csproj `
  --configuration Release `
  --no-build
```

Result:

```text
31/31 tests passed.
```

The executed cases include hashing, path containment, allow-state tamper detection, quarantine, signed integrity manifest mutation, audit mutation, Authenticode trusted/unsigned fixtures, benign AMSI, Defender event redaction, monitor outage/backoff/recovery, provider/subscriber fault isolation, shutdown and timed-wait regressions, Windows Security Center precedence, System32-only imports, PowerShell module pinning, bounded live/fallback observations, child-output limits, event-count ceilings, observation measurements, and the three Remote Assist boundary regressions.

Hosted-runner observations from this suite:

```text
Windows Security change registration:
  registered=False
  duration=10.3 ms
  detail=Unable to load DLL 'wscapi.dll' ... 0x8007007E

Windows protection health:
  WSC=Unknown
  mode=Normal
  bounded duration=6427.5 ms

Defender Operational events:
  16 events
  duration=815.8 ms

Observation means:
  WSC read=0.235 ms
  AMSI 4 KiB call=0.358 ms
```

These measurements describe one hosted run and are not performance guarantees. The `wscapi.dll` result means this host proves bounded degradation, not successful live provider enumeration.

### Supply-chain hardening suite

```powershell
dotnet run `
  --project .\tests\Soltex.Security.SupplyChain.Tests\Soltex.Security.SupplyChain.Tests.csproj `
  --configuration Release `
  --no-build
```

Result:

```text
18/18 tests passed.
```

Executed cases:

1. exact code-signing publisher identity accepted;
2. same subject with another public key rejected;
3. missing code-signing EKU rejected;
4. trusted `.NET` host accepted only through the explicitly requested primary signature and zero-secondary-signature boundary;
5. first release and monotonic upgrade accepted;
6. rollback rejected;
7. identical signed release accepted idempotently;
8. same-sequence/different-manifest equivocation rejected;
9. local authenticated sequence state detects mutation;
10. a separately held process lock causes bounded cancellation, then the same sequence-store instance recovers after release;
11. signed noncanonical `./plugin.dll` alias rejected;
12. signed non-UTC publication time rejected;
13. benign ZIP bytes and SHA-256 preserved;
14. traversal rejected and private staging cleaned;
15. case-colliding archive paths rejected;
16. symbolic-link entry rejected;
17. expanded-size ceiling enforced;
18. compression-ratio ceiling enforced.

The test certificate and key material are ephemeral fixtures. They are not Soltex production signing material and are not committed.

### Storage, publisher, ZIP, and workflow hostile suite

```powershell
dotnet run `
  --project .\tests\Soltex.Security.Hardening.Tests\Soltex.Security.Hardening.Tests.csproj `
  --configuration Release `
  --no-build
```

Result:

```text
12/12 tests passed.
```

The cases cover immutable publisher-snapshot binding, source mutation after snapshot, authenticated current/last-known-good state recovery and equivocation, strict duplicate-property JSON, bounded ZIP central-directory preflight, ZIP64/multi-disk rejection, and immutable GitHub Actions SHA/permission policy.

### Non-installing update-planner hostile suite

```powershell
dotnet run `
  --project .\tests\Soltex.Update.Tests\Soltex.Update.Tests.csproj `
  --configuration Release `
  --no-build
```

Result:

```text
17/17 tests passed.
MEASURE update_planner_suite tests=17 failed=0 total_ms=2895.6
```

The cases cover valid signed descriptors, duplicate properties, expiry classification, signature quorum, unsigned redirect origins, exact trust replay, trust rollback/equivocation, overlap-preserving trust upgrade, same-key-ID replacement, exact locked acquisition bytes, redirect/truncation/cancellation cleanup, journal sanitization, private-artifact recovery, and exact expiring confirmation.

Hosted-runner wall-clock observations:

| Observation | Duration |
|---|---:|
| Complete 17-case suite | 2,895.6 ms |
| Slowest case: descriptor quorum | 333.4 ms |
| Exact locked acquisition and cleanup | 291.9 ms |
| Unauthorized redirect rejection and cleanup | 175.5 ms |
| Truncated body rejection and cleanup | 133.0 ms |
| Cancellation cleanup | 143.4 ms |
| Journal sanitization | 105.5 ms |
| Recovery inspection and private cleanup | 96.9 ms |
| Exact confirmation semantics | 0.9 ms |

These are one-run diagnostic timings for ephemeral RSA fixtures and an in-memory authenticated transport. They are not network, disk, production-feed, installer, startup, or responsiveness guarantees.

### Device Fabric Stage 1 policy suite

```powershell
dotnet run `
  --project .\tests\Soltex.DeviceFabric.Tests\Soltex.DeviceFabric.Tests.csproj `
  --configuration Release `
  --no-build
```

Result:

```text
24/24 tests passed.
MEASURE device_fabric_suite tests=24 failed=0 total_ms=47.7
```

The cases prove the bounded model behavior for exact catalog membership, target-manifest binding, device-local read-only policy, visible per-job consent, visible external-client handoff, immutable caller-input copies, identifier/display/version validation, duplicate rejection, inventory bounds, and Windows-only Defender request declarations. They also prove bounded/sanitized local machine fields, explicit provenance, and the `NotEnrolled` state. Explicit hostile identifiers for generic shell, arbitrary download-and-execute, and hidden unattended control are denied.

This suite has no device agent, transport, listener, enrollment, signed envelope, replay store, executor, RustDesk session, or real remote action. Its wall-clock duration is one hosted-runner diagnostic, not a cross-device command-latency claim.

### Bounded Windows monitoring suite

```powershell
dotnet run `
  --project .\tests\Soltex.Monitoring.Tests\Soltex.Monitoring.Tests.csproj `
  --configuration Release `
  --no-build
```

Result:

```text
13/13 tests passed.
MEASURE monitoring_suite tests=13 failed=0 total_ms=765.9
state=Partial; processes=32; volumes=2; inaccessible=2; provider_ms=166.6; wall_ms=167.1
```

The suite covers aggregate CPU/process math, sanitization, history validation and immutable copies, safe sample-window bounds, cancellation, live Windows bounds/provenance, path omission, percentage bounds, and measured capture overhead. The representative live capture is one hosted-runner diagnostic, not a responsiveness or throughput guarantee.

### WPF control and real-binding suite

```powershell
dotnet run `
  --project .\tests\Soltex.App.Tests\Soltex.App.Tests.csproj `
  --configuration Release `
  --no-build
```

Result:

```text
5/5 tests passed.
MEASURE app_control_suite tests=5 failed=0 total_ms=1704.3
```

The STA suite checks shared controls including the slider, a pixel-rendered bounded sparkline, Home with a real telemetry snapshot, Monitoring provenance and bounded rows, Devices with exactly six modeled capabilities and `NotEnrolled`, and hero/card layout bounds. These tests do not grant subjective visual or accessibility acceptance.

### Opt-in EICAR interoperability

```powershell
$env:SOLTEX_RUN_EICAR = '1'
dotnet run `
  --project .\tests\Soltex.Security.Tests\Soltex.Security.Tests.csproj `
  --configuration Release `
  --no-build
Remove-Item Env:\SOLTEX_RUN_EICAR -ErrorAction SilentlyContinue
```

Result:

```text
31/32 tests passed.
FAIL AMSI detects the safe EICAR test marker
Installed AMSI provider did not block EICAR (result 1).
```

The marker was submitted to AMSI in memory only. No malware sample or file-system AV-evasion action was used. The workflow deliberately classifies this as a hosted-provider interoperability gap while preserving a green repository-correctness result for build, required tests, and renders. Run this check on the owner-controlled Windows machine before claiming 32/32.

### Native Home render

```powershell
dotnet run `
  --project .\src\Soltex.App\Soltex.App.csproj `
  --configuration Release `
  --no-build `
  -- `
  --render-smoke .\artifacts\visual\home-current-source.png `
  --panel home
```

Result: command succeeded and the expected 1044×788 PNG was present. Current-capture inspection found a balanced editorial hero, visible machine profile, real CPU/memory/storage/process state, and explicit GPU/network/peer gaps without gross clipping.

### Native Monitoring render

```powershell
dotnet run `
  --project .\src\Soltex.App\Soltex.App.csproj `
  --configuration Release `
  --no-build `
  -- `
  --render-smoke .\artifacts\visual\monitoring-current-source.png `
  --panel monitoring
```

Result: command succeeded and the expected 1044×788 PNG was present. Current-capture inspection confirmed readable CPU/memory, fixed-volume, provider-coverage, and process regions plus the explicit 72-sample history wording.

### Native Devices render

```powershell
dotnet run `
  --project .\src\Soltex.App\Soltex.App.csproj `
  --configuration Release `
  --no-build `
  -- `
  --render-smoke .\artifacts\visual\devices-current-source.png `
  --panel devices
```

Result: command succeeded and the expected 1044×788 PNG was present. Current-capture inspection confirmed the local profile, explicit unenrolled state, visible external-client handoff, wrapped private-mesh copy, and six modeled capability cards.

### Native Security render

```powershell
dotnet run `
  --project .\src\Soltex.App\Soltex.App.csproj `
  --configuration Release `
  --no-build `
  -- `
  --render-smoke .\artifacts\visual\security-current-source.png `
  --panel security
```

Result: command succeeded and the expected 1044×788 PNG was present. Pixel inspection confirmed that the scan subtitle is fully visible and Defender event details wrap with tooltips instead of being truncated.

### Native Remote Assist render

```powershell
dotnet run `
  --project .\src\Soltex.App\Soltex.App.csproj `
  --configuration Release `
  --no-build `
  -- `
  --render-smoke .\artifacts\visual\remote-assist-current-source.png `
  --panel remote
```

Result: command succeeded and the expected 1044×788 PNG was present.

### Native Updates render

```powershell
dotnet run `
  --project .\src\Soltex.App\Soltex.App.csproj `
  --configuration Release `
  --no-build `
  -- `
  --render-smoke .\artifacts\visual\updates-current-source.png `
  --panel update
```

Result: command succeeded and the expected 1044×788 PNG was present. Pixel inspection confirmed the Soltex branding, selected Updates navigation state, honest disabled planning control, authenticated-journal empty state, and explicit non-installing/recovery boundaries without visible truncation in the captured viewport.

The verification step found all six PNGs and no `*.error.txt` render output. Frozen screenshot hashes:

| Panel | SHA-256 |
|---|---|
| Home | `4732077CA3053349DC359117ECE032B0A5E75313384F61A536C6932887DAB1FC` |
| Monitoring | `F6F6D9C989B0538EA9472B26615CA5CD7A7EAC5CBBC5515D7B907DBBAA326473` |
| Devices | `8B8F9AE4147267D2455B93311CE38004D8F68E59813CEB7D6384FF6A3AD47084` |
| Security | `BD2D700B6DCE5588968A06AF6F54C3B47985DF0E4D32530C625372AAFD70DBBC` |
| Remote Assist | `3F403752B64EE24755B00AB9BDBB1C1787B8FB0A10368A3604D4544950C27E70` |
| Updates | `124F4835984FB0283D0A73312AB43BDE63283B5BACBEA4A55170319EDB9717CC` |

These captures are current native initialization evidence at one viewport. Human Eye verdict for this bounded capture set is `KEEP`, with no gross layout blocker observed after final correction. The review was not source-naive, and it does not cover 1366×768, 1440p, 4K, 100/125/150/200-percent scaling, reduced motion, contrast thresholds, a full keyboard/screen-reader audit, or owner visual approval.

## Owner-controlled Windows gate

Run from an ordinary unelevated PowerShell session unless a separately documented step explicitly requires elevation:

```powershell
Set-Location 'C:\Users\suhai\Documents\soltex-immersive-workspace'

# Local identity and preservation checks
git remote -v
git fetch origin main
git branch --show-current
git rev-parse HEAD
git log --oneline -1 origin/main
git status --short --branch
git diff --stat origin/main...HEAD

# Running-process ownership check
Get-Process Soltex,dotnet -ErrorAction SilentlyContinue |
  Select-Object Id, ProcessName, Path, StartTime

# Read the current evidence ledger before mutation
Get-Content .\docs\IMPLEMENTATION_STATUS.md

# Required build and tests
.\eng\verify-identity.ps1
dotnet build .\Soltex.sln --configuration Release
dotnet run --project .\tests\Soltex.Security.Tests\Soltex.Security.Tests.csproj --configuration Release --no-build
dotnet run --project .\tests\Soltex.Security.SupplyChain.Tests\Soltex.Security.SupplyChain.Tests.csproj --configuration Release --no-build
dotnet run --project .\tests\Soltex.Security.Hardening.Tests\Soltex.Security.Hardening.Tests.csproj --configuration Release --no-build
dotnet run --project .\tests\Soltex.Update.Tests\Soltex.Update.Tests.csproj --configuration Release --no-build
dotnet run --project .\tests\Soltex.DeviceFabric.Tests\Soltex.DeviceFabric.Tests.csproj --configuration Release --no-build
dotnet run --project .\tests\Soltex.Monitoring.Tests\Soltex.Monitoring.Tests.csproj --configuration Release --no-build
dotnet run --project .\tests\Soltex.App.Tests\Soltex.App.Tests.csproj --configuration Release --no-build

# Optional provider-interoperability check
$env:SOLTEX_RUN_EICAR = '1'
dotnet run --project .\tests\Soltex.Security.Tests\Soltex.Security.Tests.csproj --configuration Release --no-build
Remove-Item Env:\SOLTEX_RUN_EICAR -ErrorAction SilentlyContinue

# Current native renders
New-Item -ItemType Directory -Force .\artifacts\visual | Out-Null
dotnet run --project .\src\Soltex.App\Soltex.App.csproj --configuration Release --no-build -- --render-smoke .\artifacts\visual\home-current-source.png --panel home
dotnet run --project .\src\Soltex.App\Soltex.App.csproj --configuration Release --no-build -- --render-smoke .\artifacts\visual\monitoring-current-source.png --panel monitoring
dotnet run --project .\src\Soltex.App\Soltex.App.csproj --configuration Release --no-build -- --render-smoke .\artifacts\visual\devices-current-source.png --panel devices
dotnet run --project .\src\Soltex.App\Soltex.App.csproj --configuration Release --no-build -- --render-smoke .\artifacts\visual\security-current-source.png --panel security
dotnet run --project .\src\Soltex.App\Soltex.App.csproj --configuration Release --no-build -- --render-smoke .\artifacts\visual\remote-assist-current-source.png --panel remote
dotnet run --project .\src\Soltex.App\Soltex.App.csproj --configuration Release --no-build -- --render-smoke .\artifacts\visual\updates-current-source.png --panel update
```

Before editing locally, resolve rather than overwrite any dirty files, untracked files, commits not on the remote branch, or running processes that own build outputs. Do not use `git reset --hard`, `git clean`, forced checkout, or force push as a convenience.

## Supply-chain hostile-fixture policy

Permitted test material:

- ephemeral self-signed test certificates and keys created in memory;
- Microsoft-signed `.NET` host already installed on the runner;
- benign random/text payloads;
- a locally held lock-file handle representing another Soltex process;
- ZIP metadata fixtures representing traversal, case collision, symbolic link, size, and ratio boundaries;
- the standardized harmless EICAR marker submitted to AMSI in memory only.

Not used or permitted in this gate:

- live malware;
- antivirus exclusions or security-setting changes;
- signature harvesting from third-party products;
- unsigned executable launch from staging;
- archive exploitation outside the private staging root;
- remote credentials, unattended access, elevation, service installation, hidden sessions, or public listeners.

## Owner-host gate

Integration head `767a64abd7e1a9c0a3c73bbc8d2b4cb510539a20`, run on the owner Windows machine rather than a hosted runner.

Environment:

- Windows 10.0.26200, x64;
- .NET SDK 10.0.302;
- ordinary unelevated PowerShell session;
- a registered antivirus provider active, so `wscapi.dll` and the AMSI provider were both reachable.

| Gate | Result |
|---|---|
| Release build | passed, 0 warnings, 0 errors |
| Security | 31/31 |
| Supply chain | 18/18 |
| Hardening | 12/12 |
| Update planner | 17/17 |
| Device Fabric | 24/24 |
| Monitoring | 13/13 |
| WPF controls | 5/5 |
| Opt-in EICAR | **32/32** |
| Native renders | six PNGs, no render-error file |

The EICAR lane passed here because this host's registered AMSI provider blocked the in-memory marker, where the hosted runner returned native result `1` and capped the lane at 31/32. That difference is the documented provider-interoperability gap closing on a machine with a working provider. It is evidence about this host's AMSI integration only — not a Soltex detection rate, efficacy measurement, or antivirus-product claim — and it does not transfer to hosts with a different or absent provider.

## Desktop release build

`eng\publish-release.ps1` regenerates the application icon, publishes a self-contained single-file `win-x64` build, and compiles the per-user installer:

```powershell
.\eng\publish-release.ps1 -Version 1.0.0
```

Version 1.0.0 was produced from merge commit `ac7eef304b956012ba21929d8b7afba9c9b9dd1c`. The published executable carries `ProductVersion 1.0.0+ac7eef304b956012ba21929d8b7afba9c9b9dd1c`, so a shipped binary identifies its exact source commit.

Installer behavior verified on the owner host:

| Check | Result |
|---|---|
| Silent install exit code | 0, no elevation prompt |
| Install location | `%LocalAppData%\Programs\Soltex` |
| Add/Remove Programs record | `Soltex 1.0.0`, publisher `Soltex` |
| Start Menu shortcuts | `Soltex.lnk` and `Uninstall Soltex.lnk`, correct targets |
| Installed binary render-smoke | Security panel PNG produced |
| Interactive launch | real WPF window titled `Soltex` |
| Clean shutdown | window closed, process exited |
| Silent uninstall | exit 0, program directory and ARP record removed, no leftovers |

Boundaries for this build:

- the installer is **not code-signed**, so SmartScreen warns on first run and the publisher shows as unknown;
- installation is per-user by design, matching the `asInvoker` manifest; there is no machine-wide, service, or scheduled-task component;
- uninstall removes program files and shortcuts only. Per-user Soltex state is deliberately retained so an accidental uninstall cannot destroy authenticated quarantine, audit-chain, release-sequence, or planning-journal history;
- this is a first-install package. It is not the signed, self-updating release path, and the update planner still cannot install, activate, or roll back anything.

## Pending release evidence

The following must exist before any signed installer/update claim:

- selected production code-signing and manifest-signing identities;
- documented key custody, backup, loss, revocation, and recovery procedures;
- committed public metadata/release keys, TLS/publisher pins, and a signed trust-policy rollout using the implemented overlap/retirement rules;
- a production authenticated descriptor source and release-candidate evidence using real public release identities;
- a **signed** installer package; the unsigned package and its deterministic install/uninstall evidence are recorded above, but no code-signing identity is applied;
- atomic activation and interrupted-update rollback/recovery tests;
- installer-bound tampered package, concurrent process, lock timeout, pin mismatch, expired/revoked certificate, partial I/O, disk-full, locked-file, reboot, downgrade, and cancellation evidence;
- retained hashes, signatures, logs, and exact reproduction commands for a release candidate.
