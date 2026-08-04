# Validation and evidence

Snapshot: 2026-08-04
Repository: `slaveofsolace/soltex`  
Canonical product: Soltex  
Current solution identifier: `WaveSlate.sln`

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
252fd9fd5314e5403e7abd060855b19468bc2719
```

GitHub pull-request merge preview exercised by the run:

```text
e464be421851523f48a512fdf9ce42b75dad750d
```

Base `main` commit:

```text
39eaf628f6add6c89963a81b7c1971c2f74f02a1
```

Environment:

- GitHub Actions run `30915008164`;
- job `92010795630`;
- Windows Server 2025, build `10.0.26100`;
- runner image `windows-2025-vs2026`, version `20260728.188.1`;
- .NET SDK `10.0.302`;
- checkout directory `D:\a\soltex\soltex`.

Retained evidence:

- artifact name: `soltex-windows-evidence-30915008164-1`;
- artifact ID: `8894704016`;
- uploaded artifact ZIP size: 323,145 bytes;
- GitHub-recorded artifact ZIP SHA-256: `9D90D3E98A4DF9235636DFA1185894B527038EFB242007DEE3ED82E6FEBC376C`;
- configured retention: 30 days;
- uploaded files: build log, five focused-suite logs, gate classification, Security PNG, Remote Assist PNG, and Updates PNG.

## Commands exercised by the workflow

### Release build

```powershell
dotnet build .\WaveSlate.sln --configuration Release
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
  --project .\tests\WaveSlate.Security.Tests\WaveSlate.Security.Tests.csproj `
  --configuration Release `
  --no-build
```

Result:

```text
27/27 tests passed.
```

The executed cases include hashing, path containment, allow-state tamper detection, quarantine, signed integrity manifest mutation, audit mutation, Authenticode trusted/unsigned fixtures, benign AMSI, Defender event redaction, monitor outage/backoff/recovery, provider/subscriber fault isolation, shutdown and timed-wait regressions, Windows Security Center precedence, System32-only imports, PowerShell module pinning, bounded live/fallback observations, child-output limits, event-count ceilings, observation measurements, and the three Remote Assist boundary regressions.

Hosted-runner observations from this suite:

```text
Windows Security change registration:
  registered=False
  duration=8.6 ms
  detail=Unable to load DLL 'wscapi.dll' ... 0x8007007E

Windows protection health:
  WSC=Unknown
  mode=Normal
  bounded duration=5121.1 ms

Defender Operational events:
  16 events
  duration=740.5 ms

Observation means:
  WSC read=0.152 ms
  AMSI 4 KiB call=0.326 ms
```

These measurements describe one hosted run and are not performance guarantees. The `wscapi.dll` result means this host proves bounded degradation, not successful live provider enumeration.

### Supply-chain hardening suite

```powershell
dotnet run `
  --project .\tests\WaveSlate.Security.SupplyChain.Tests\WaveSlate.Security.SupplyChain.Tests.csproj `
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
  --project .\tests\WaveSlate.Security.Hardening.Tests\WaveSlate.Security.Hardening.Tests.csproj `
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
  --project .\tests\WaveSlate.Update.Tests\WaveSlate.Update.Tests.csproj `
  --configuration Release `
  --no-build
```

Result:

```text
17/17 tests passed.
MEASURE update_planner_suite tests=17 failed=0 total_ms=2602.5
```

The cases cover valid signed descriptors, duplicate properties, expiry classification, signature quorum, unsigned redirect origins, exact trust replay, trust rollback/equivocation, overlap-preserving trust upgrade, same-key-ID replacement, exact locked acquisition bytes, redirect/truncation/cancellation cleanup, journal sanitization, private-artifact recovery, and exact expiring confirmation.

Hosted-runner wall-clock observations:

| Observation | Duration |
|---|---:|
| Complete 17-case suite | 2,602.5 ms |
| Slowest case: descriptor quorum | 260.4 ms |
| Exact locked acquisition and cleanup | 237.8 ms |
| Unauthorized redirect rejection and cleanup | 107.8 ms |
| Truncated body rejection and cleanup | 127.0 ms |
| Cancellation cleanup | 136.0 ms |
| Journal sanitization | 78.4 ms |
| Recovery inspection and private cleanup | 81.3 ms |
| Exact confirmation semantics | 1.0 ms |

These are one-run diagnostic timings for ephemeral RSA fixtures and an in-memory authenticated transport. They are not network, disk, production-feed, installer, startup, or responsiveness guarantees.

### Opt-in EICAR interoperability

```powershell
$env:WAVESLATE_RUN_EICAR = '1'
dotnet run `
  --project .\tests\WaveSlate.Security.Tests\WaveSlate.Security.Tests.csproj `
  --configuration Release `
  --no-build
Remove-Item Env:\WAVESLATE_RUN_EICAR -ErrorAction SilentlyContinue
```

Result:

```text
27/28 tests passed.
FAIL AMSI detects the safe EICAR test marker
Installed AMSI provider did not block EICAR (result 1).
```

The marker was submitted to AMSI in memory only. No malware sample or file-system AV-evasion action was used. The workflow deliberately classifies this as a hosted-provider interoperability gap while preserving a green repository-correctness result for build, required tests, and renders. Run this check on the owner-controlled Windows machine before claiming 28/28.

### Native Security render

```powershell
dotnet run `
  --project .\src\WaveSlate.App\WaveSlate.App.csproj `
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
  --project .\src\WaveSlate.App\WaveSlate.App.csproj `
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
  --project .\src\WaveSlate.App\WaveSlate.App.csproj `
  --configuration Release `
  --no-build `
  -- `
  --render-smoke .\artifacts\visual\updates-current-source.png `
  --panel update
```

Result: command succeeded and the expected 1044×788 PNG was present. Pixel inspection confirmed the Soltex branding, selected Updates navigation state, honest disabled planning control, authenticated-journal empty state, and explicit non-installing/recovery boundaries without visible truncation in the captured viewport.

The verification step found all three PNGs and no `*.error.txt` render output. These captures are current native initialization evidence at one viewport. They do not cover 1366×768, 1440p, 4K, 100/125/150/200-percent scaling, reduced motion, a full keyboard/screen-reader audit, or owner visual approval.

## Owner-controlled Windows gate

Run from an ordinary unelevated PowerShell session unless a separately documented step explicitly requires elevation:

```powershell
Set-Location 'C:\Users\suhai\Documents\SOL Tools'

# Local identity and preservation checks
git remote -v
git fetch origin main
git branch --show-current
git rev-parse HEAD
git log --oneline -1 origin/main
git status --short --branch
git diff --stat origin/main...HEAD

# Running-process ownership check
Get-Process WaveSlate,dotnet -ErrorAction SilentlyContinue |
  Select-Object Id, ProcessName, Path, StartTime

# Read the current evidence ledger before mutation
Get-Content .\docs\IMPLEMENTATION_STATUS.md

# Required build and tests
dotnet build .\WaveSlate.sln --configuration Release
dotnet run --project .\tests\WaveSlate.Security.Tests\WaveSlate.Security.Tests.csproj --configuration Release --no-build
dotnet run --project .\tests\WaveSlate.Security.SupplyChain.Tests\WaveSlate.Security.SupplyChain.Tests.csproj --configuration Release --no-build

# Optional provider-interoperability check
$env:WAVESLATE_RUN_EICAR = '1'
dotnet run --project .\tests\WaveSlate.Security.Tests\WaveSlate.Security.Tests.csproj --configuration Release --no-build
Remove-Item Env:\WAVESLATE_RUN_EICAR -ErrorAction SilentlyContinue

# Current native renders
New-Item -ItemType Directory -Force .\artifacts\visual | Out-Null
dotnet run --project .\src\WaveSlate.App\WaveSlate.App.csproj --configuration Release --no-build -- --render-smoke .\artifacts\visual\security-current-source.png --panel security
dotnet run --project .\src\WaveSlate.App\WaveSlate.App.csproj --configuration Release --no-build -- --render-smoke .\artifacts\visual\remote-assist-current-source.png --panel remote
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

## Pending release evidence

The following must exist before any signed installer/update claim:

- selected production code-signing and manifest-signing identities;
- documented key custody, backup, loss, revocation, and recovery procedures;
- committed public metadata/release keys, TLS/publisher pins, and a signed trust-policy rollout using the implemented overlap/retirement rules;
- a production authenticated descriptor source and release-candidate evidence using real public release identities;
- signed installer package and deterministic uninstall evidence;
- atomic activation and interrupted-update rollback/recovery tests;
- installer-bound tampered package, concurrent process, lock timeout, pin mismatch, expired/revoked certificate, partial I/O, disk-full, locked-file, reboot, downgrade, and cancellation evidence;
- retained hashes, signatures, logs, and exact reproduction commands for a release candidate.
