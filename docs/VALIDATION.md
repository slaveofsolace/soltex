# Validation and evidence

Snapshot: 2026-08-03  
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
6ea85727935564122c5237ae9c3b85cd81cbbc72
```

GitHub pull-request merge preview exercised by the run:

```text
811b55ad875c79d3b2c50f746ae853c291ad210e
```

Base `main` commit:

```text
eb0d2ffeacc52d16b46795cd4facdd17a5816b32
```

Environment:

- GitHub Actions run `30858289994`;
- job `91834318247`;
- Windows Server 2025, build `10.0.26100`;
- runner image `windows-2025-vs2026`, version `20260728.188.1`;
- .NET SDK `10.0.302`;
- checkout directory `D:\a\soltex\soltex`.

Retained evidence:

- artifact name: `soltex-windows-evidence-30858289994-1`;
- artifact ID: `8873339056`;
- downloaded artifact ZIP size: 224,240 bytes;
- uncompressed evidence payload: 242,129 bytes across seven files;
- artifact ZIP SHA-256: `B2720273046CA62D9D5D675AEA13E48CE6D5CAE35ACAB95F5B3725A13B68538C`;
- configured retention: 30 days;
- uploaded files: build log, existing-test log, supply-chain-test log, EICAR log, gate classification, Security PNG, Remote Assist PNG.

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
  duration=8.7 ms
  detail=Unable to load DLL 'wscapi.dll' ... 0x8007007E

Windows protection health:
  WSC=Unknown
  mode=Normal
  bounded duration=5713.6 ms

Defender Operational events:
  16 events
  duration=735.0 ms

Observation means:
  WSC read=0.344 ms
  AMSI 4 KiB call=0.379 ms
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

Result: command succeeded and the expected 1044×788 PNG was present.

Pixel inspection found no render exception, but the current scan subtitle and event-detail column contain visible truncation. This is recorded as an open UI defect rather than accepted polish.

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

The verification step found both PNGs and no `*.error.txt` render output. These captures are current native initialization evidence at one viewport. They do not cover 1366×768, 1440p, 4K, 100/125/150/200-percent scaling, reduced motion, full keyboard navigation, text alternatives, or owner visual approval.

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
- committed public pins/keys with an explicit rotation mechanism;
- signed installer package and deterministic uninstall evidence;
- authenticated update acquisition and bounded download limits;
- a composed update planner and transaction;
- atomic activation and interrupted-update rollback/recovery tests;
- tampered manifest/package, stale sequence, concurrent process, lock timeout, pin mismatch, expired/revoked certificate, partial I/O, disk-full, locked-file, reboot, downgrade, and cancellation evidence;
- retained hashes, signatures, logs, and exact reproduction commands for a release candidate.
