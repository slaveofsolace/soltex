# Validation and evidence

Snapshot: 2026-08-05  
Repository: `slaveofsolace/soltex`  
Branch: `audit/soltex-v1-quality-pass`  
Commit: `a2ac7718fd207364c9a74199585a9720ae53821f`

## Source/build/test/render gate

GitHub Actions:

```text
run: 31065075683
job: 92500998931
```

Environment:

- Windows Server 2025;
- hosted runner label `windows-latest`;
- .NET 10 SDK selected by the pinned setup action;
- immutable checkout/setup/upload action SHAs;
- `persist-credentials: false`;
- workflow permission: `contents: read`.

Results:

| Command/gate | Result |
|---|---:|
| `dotnet build .\Soltex.sln --configuration Release` | Passed, 0 warnings, 0 errors |
| `Soltex.Security.Tests` | 31/31 |
| `Soltex.Security.SupplyChain.Tests` | 18/18 |
| `Soltex.Security.Hardening.Tests` | 12/12 |
| `Soltex.Update.Tests` | 17/17 |
| `Soltex.DeviceFabric.Tests` | 24/24 |
| `Soltex.Monitoring.Tests` | 15/15 |
| `Soltex.App.Tests` | 7/7 |
| hosted opt-in EICAR/AMSI | 31/32 |
| Home render | Passed |
| Monitoring render | Passed |
| Devices render | Passed |
| Security render | Passed |
| Remote Assist render | Passed |
| Updates render | Passed |
| render file/error verification | Passed |

The hosted EICAR result is not a Soltex efficacy test. The installed hosted AMSI provider returned native result `1` for the in-memory marker. Repository owner-host evidence separately reports 32/32 on its identified Windows environment.

Retained artifact:

```text
name: soltex-windows-evidence-31065075683-1
artifact ID: 8953553034
size: 644475 bytes
digest: sha256:850946fd3fc57e7a318565ca26428627cb3caa6a822409fd2112f5a14af9d4ff
```

## Portable package-smoke gate

GitHub Actions:

```text
run: 31065075715
job: 92498644583
```

The workflow:

1. published `Soltex.App` self-contained and single-file for `win-x64`;
2. skipped installer creation;
3. launched the published `Soltex.exe`;
4. requested a native Home render;
5. verified the PNG and absence of an error sidecar;
6. recorded file length, SHA-256, and Authenticode status;
7. retained the executable and evidence.

Results:

```text
file: Soltex.exe
length: 71485765 bytes
sha256: E3AE863B30A16EB091EF8C73EB6985EEC9C15760EBC5114F775FB44333A5F2F5
Authenticode: NotSigned
published render: Passed
```

Retained artifact:

```text
name: soltex-package-smoke-31065075715-1
artifact ID: 8953553036
size: 65756982 bytes
digest: sha256:c48026fc3b2bb011b42563c2cd7b32eb5795bb9dbf111d7bc4324d0c14523088
```

The package run is launch evidence, not trusted-distribution evidence. No code-signing identity or timestamp is configured.

## Implemented audit regressions

### Network telemetry

- byte-rate math rejects counter regression and zero elapsed time;
- process/interface display names remove control characters and enforce length bounds;
- active-interface count is bounded;
- unavailable interface sampling is explicit;
- aggregate and per-interface rates are nonnegative;
- network provenance is included in the snapshot;
- Home and Monitoring render both sampled and unavailable states;
- throughput sparklines auto-scale without affecting percentage charts.

### Telemetry lifecycle

Focused policy tests prove that telemetry:

- runs on visible Home;
- runs on visible Monitoring;
- stops on hidden pages;
- stops while minimized;
- cannot restart while closing.

### Package evidence

The package lane fails if:

- publish fails;
- the executable cannot launch;
- native render fails or produces an error sidecar;
- the executable is missing;
- Authenticode returns a state other than `Valid` or truthfully `NotSigned`.

## Owner-controlled Windows commands

Run from an ordinary PowerShell session after checking local branch, HEAD, dirty state, and running processes:

```powershell
Set-Location 'C:\Users\suhai\Documents\SOL Tools'

git remote -v
git fetch origin
git branch --show-current
git rev-parse HEAD
git status --short --branch
Get-Process Soltex,dotnet -ErrorAction SilentlyContinue |
  Select-Object Id,ProcessName,Path,StartTime

dotnet build .\Soltex.sln --configuration Release

dotnet run --project .\tests\Soltex.Security.Tests\Soltex.Security.Tests.csproj --configuration Release --no-build
dotnet run --project .\tests\Soltex.Security.SupplyChain.Tests\Soltex.Security.SupplyChain.Tests.csproj --configuration Release --no-build
dotnet run --project .\tests\Soltex.Security.Hardening.Tests\Soltex.Security.Hardening.Tests.csproj --configuration Release --no-build
dotnet run --project .\tests\Soltex.Update.Tests\Soltex.Update.Tests.csproj --configuration Release --no-build
dotnet run --project .\tests\Soltex.DeviceFabric.Tests\Soltex.DeviceFabric.Tests.csproj --configuration Release --no-build
dotnet run --project .\tests\Soltex.Monitoring.Tests\Soltex.Monitoring.Tests.csproj --configuration Release --no-build
dotnet run --project .\tests\Soltex.App.Tests\Soltex.App.Tests.csproj --configuration Release --no-build

.\eng\verify.ps1 -RunEicar
.\eng\publish-release.ps1 -Version '0.0.0-local' -Runtime 'win-x64' -SkipInstaller
```

Do not use `git reset --hard`, `git clean`, forced checkout, or force push to resolve local differences.

## Evidence limits

- hosted runner labels and installed providers can change;
- one successful render is not visual approval or accessibility conformance;
- one package launch is not install/upgrade/uninstall evidence;
- CI timing and memory observations are diagnostic only;
- an unsigned package should not be represented as trusted public distribution;
- owner-host and hosted EICAR results must remain separate.
