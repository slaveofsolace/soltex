# Validation and evidence

Snapshot: 2026-08-09

Repository: `slaveofsolace/soltex`

Merged baseline: `main` at `61639bfe7ea3a7191fcb6c70ea1f2a699aa77248` (PR #9)

Continuation: `sol/soltex-ui-evidence-matrix`

## Complete UI evidence-matrix continuation

The continuation does not modify the native interface. It makes the Windows evidence boundary complete and its Git provenance explicit.

Local workflow-equivalent validation on the owner-controlled Windows host:

| Command/gate | Result |
|---|---:|
| `dotnet build .\Soltex.sln --configuration Release` | Passed, 0 warnings, 0 errors |
| identity policy | Passed; 159 tracked text files, 9 reasoned allowlist entries |
| design-token policy | Passed; 7 XAML files |
| `Soltex.Security.Tests` | 31/31 |
| `Soltex.Security.SupplyChain.Tests` | 18/18 |
| `Soltex.Security.Hardening.Tests` | 12/12 |
| `Soltex.Update.Tests` | 17/17 |
| `Soltex.DeviceFabric.Tests` | 24/24 |
| `Soltex.Monitoring.Tests` | 15/15 |
| `Soltex.Audio.Tests` | 10/10 |
| `Soltex.App.Tests` | 10/10 |
| complete native matrix | 11/11 at 1280x820 |
| stale-output negative path | Failed closed before capture |
| mismatched-tested-commit negative path | Failed closed before capture |
| self-contained `win-x64` publish | Passed; length and SHA-256 recorded in generated evidence |
| published package native Home render | Passed; waited process exit code 0 |
| package identity schema 2 | Passed; executable/render hashes and 1280x820 dimensions recorded; Authenticode `NotSigned` |
| package stale/identity negative paths | Failed closed before evidence write |

The matrix contains all eight default workspaces plus expanded Monitoring details, Mixer endpoints, and Security activity. `eng/capture-ui-evidence.ps1` rejects stale outputs, render error sidecars, missing/empty files, wrong dimensions, checked-out/tested-commit mismatch, and unavailable source-head ancestry. Its manifest records each artifact's byte size and SHA-256 digest together with separate `source_head_sha` and `tested_commit_sha` values.

The first PR run exposed and correctly rejected a hosted 1044x788 capture. The normal-chrome WPF window had inherited the hosted work-area limit. Render-smoke now configures the same complete Window visual as a nonactivating, borderless, nonresizable popup fixed to 1280x820. A focused regression starts from a 1044x788 request, exercises the real off-screen popup path, and verifies the canonical bitmap plus unclipped bottom-right content. The workspace column also clips its descendants, while navigation labels bind directly to their owning buttons.

`eng/record-package-identity.ps1` gives package-smoke schema version 2 the same source/tested distinction while retaining `commit` as a compatibility alias for the tested checkout. It also binds the retained Home render by dimensions, length, and SHA-256. Both workflows use checkout depth 2 for pull-request ancestry proof. The launch step reads an explicit waited process exit code rather than relying on an interactive shell to populate `$LASTEXITCODE` for a GUI executable.

The exact published-head GitHub run remains a merge gate for this continuation. See [`evidence/2026-08-09-ui-evidence-matrix`](evidence/2026-08-09-ui-evidence-matrix/README.md) for the matrix and nonclaims.

## Historical quality-audit gate (2026-08-05)

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
.\eng\capture-ui-evidence.ps1 `
  -OutputDirectory .\artifacts\visual\manual-fresh `
  -ValidationDirectory .\artifacts\validation\manual-fresh `
  -SourceHeadSha (git rev-parse HEAD) `
  -TestedCommitSha (git rev-parse HEAD)
.\eng\publish-release.ps1 -Version '0.0.0-local' -Runtime 'win-x64' -SkipInstaller
```

The capture script deliberately refuses to overwrite an existing evidence target. Use a fresh task-owned directory for each run. If local policy blocks repository scripts, invoke it from an owner-approved PowerShell process with an appropriate execution-policy boundary; do not weaken machine-wide policy for this task.

Do not use `git reset --hard`, `git clean`, forced checkout, or force push to resolve local differences.

## Evidence limits

- hosted runner labels and installed providers can change;
- one successful render is not visual approval or accessibility conformance;
- one viewport and software-rendered WPF path do not prove resizing, DPI, hardware-rendering, or multi-display behavior;
- one package launch is not install/upgrade/uninstall evidence;
- CI timing and memory observations are diagnostic only;
- an unsigned package should not be represented as trusted public distribution;
- owner-host and hosted EICAR results must remain separate.
