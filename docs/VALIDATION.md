# Validation and evidence

Snapshot: 2026-08-12

Repository: `slaveofsolace/soltex`

Current draft: `sol/soltex-product-rebuild` / [PR #11](https://github.com/slaveofsolace/soltex/pull/11)

## Audio candidate exact-source owner-host gate

Audio source `3511ba92bde450ffac4e3fad145d9a13b8986e72` was exercised on the owner-controlled Windows host through the verified portable .NET SDK 10.0.302. No Windows security setting, endpoint assignment, third-party session, SteelSeries process, Voicemeeter process, or audible media was changed.

| Command/gate | Result |
|---|---:|
| Release `Soltex.Audio` build | Passed, 0 warnings / 0 errors |
| Release `Soltex.App` build | Passed, 0 warnings / 0 errors |
| standard `Soltex.Audio.Tests` | 20/20 |
| opt-in controlled live-write `Soltex.Audio.Tests` | 21/21 |
| `Soltex.App.Tests` | 19/19 |
| live session observation | 5 exposed / 28 observed / 0 inaccessible / 0 omitted; about 8 ms provider time |
| targeted native Mixer captures | default and expanded device inventory passed at 1280x820 and were directly inspected |

The complete exact-source gate also passed Security/EICAR 32/32, supply chain 18/18, hardening 12/12, Updates 17/17, Device Fabric 24/24, Monitoring 16/16, App/control 19/19, identity 181 files/9 allowlist entries, design-token policy across 10 XAML files, and a 14/14 native matrix with every retained image at 1280x820. The first tracked verification completed green but exposed a blank tracker exit-code property; after confirming no process remained, one handle-pinned retry recorded every exit as `0`.

The opt-in write gate creates a one-second silent WinMM loop owned by the test process, locates only that process's session, writes its already-observed volume and mute values back unchanged, verifies both through immediate Core Audio read-back, stops the loop, and removes its temporary WAV. The standard suite does not perform any live write. COM GUID/vtable order, bounds, sanitization, identity privacy, rejection, target drift, cancellation, fake read-back match/mismatch, live observation, and measured overhead have focused tests.

The exact self-contained `win-x64` owner-host package is 71,559,764 bytes with SHA-256 `31ef5e44b895738723b849705247fb2a4756a2a2092ec9aa514eea429aa64ebb`; its hidden Home render launch exited `0`, produced 1280x820 evidence, and the executable is truthfully `NotSigned`. Full evidence paths and image identities are recorded in [`evidence/2026-08-11-product-rebuild/AUDIO_SESSION_GATE.md`](evidence/2026-08-11-product-rebuild/AUDIO_SESSION_GATE.md).

Published PR head `abf1dc5e58ca7a23ef57a975c7fdeb041ea4d183` then passed Windows run `31562540695` and package-smoke run `31562540694`. Downloaded artifacts `9128308988` and `9128281441` independently matched GitHub digests `sha256:dd0c089596cbbdd09f79140bf9251770dcf18e482ec46bbef3ccd5aa6537e386` and `sha256:ff0df6759cd4b9ff1c8387d174c74d347f5774b1bb446f9e8e4a11c0d68f12c6`. The tested PR merge was exactly one commit ahead of the source with `abf1dc5` as merge base. All 14 retained 1280x820 PNGs matched their manifest identities; default/expanded Audio and packaged Home were directly inspected. The hosted package executable was 71,581,365 bytes, SHA-256 `898fd206951392c837c37dcd0b41178320ab1fd23cc7376819a3f4fb920132a3`, launched with exit code 0, and remained `NotSigned`. Optional hosted AMSI/EICAR remained 31/32 because the installed provider returned native result `1`; required gates passed and no detection-efficacy claim is made.

## Services and runtime-lifecycle development gate

The current uncommitted candidate was exercised on the owner-controlled Windows host before exact-source publication:

| Command/gate | Result |
|---|---:|
| targeted Release app/test build | Passed, 0 warnings / 0 errors |
| `Soltex.App.Tests` | 24/24 |
| live read-only SCM capture | 296 exposed / 296 observed / 0 inaccessible / 0 omitted; about 65 ms |
| runtime probe schema | 2; full source/tested identities required |
| startup completion | 2,294.6 ms |
| navigation | 18 transitions; 49.3 ms mean / 327.6 ms maximum |
| visible idle CPU | 0.821% normalized |
| minimize transition CPU | 2.375% normalized; top work was not the UI dispatcher |
| minimized steady CPU | 0.000% normalized; Performance sampler stopped |
| hidden notification-area CPU | 0.000% normalized; Performance sampler stopped |

This is developmental evidence because the report identities name the last clean commit while the measured service/runtime source was still dirty. It is retained to diagnose lifecycle behavior, not to establish exact-source acceptance. The final exact-source gate must rerun after the implementation commit. Short samples are regression evidence, not hardware benchmark scores or cross-machine guarantees. See [`RUNTIME_AND_SERVICES.md`](RUNTIME_AND_SERVICES.md).

## Activity candidate owner-host gate

The Activity source candidate was exercised on the owner-controlled Windows host before publication. The gate used the repository-pinned .NET 10 contract through a verified portable .NET SDK 10.0.302 and did not disable, exclude, or reconfigure Windows security.

| Command/gate | Result |
|---|---:|
| identity policy | Passed; 172 tracked text files, 9 reasoned allowlist entries |
| Release solution build | Passed |
| `Soltex.Security.Tests` | 31/31 |
| `Soltex.Monitoring.Tests` | 16/16 |
| `Soltex.App.Tests` | 19/19 |
| Activity storage/privacy test | Passed inside the app suite |

The Activity test covers the 120-entry bound, session-only no-file default, explicit retained persistence, drive/UNC-path sanitization, durable round trip, confirmed deletion boundary, invalid JSON recovery, and oversized-file fail-closed behavior. The view test covers retention state, filtering/no-match disclosure, deletion routing to the main-window confirmation owner, and native WPF rendering. A passing test does not establish human visual acceptance.

### Exact-head hosted Activity gate

PR head `4437db80b844dcfe33cd8265e41cb5fcf00bfd76` passed Windows run `31560242046` and package-smoke run `31560242019`. Windows artifact `9127496208` has digest `sha256:0046bc2b99c0e10accf58b3c030025c4802ceee782553a06f7c3881599e969c9`; package artifact `9127485263` has digest `sha256:dfd3b7aed215a2646bac1ad745e431511f5e4de4958b479b9a8d3865865ab30b`.

The downloaded Windows ZIP independently matched its GitHub digest. Its render manifest binds source head `4437db8` to tested PR merge `4b3fff8`, records 14 native 1280×820 states, and all retained file lengths, SHA-256 values, and dimensions independently matched. Hosted Activity and Settings captures were directly inspected. Required suites passed: Security 31/31, supply chain 18/18, hardening 12/12, update 17/17, Device Fabric 24/24, monitoring 16/16, audio 10/10, and app/control 19/19. The optional hosted AMSI/EICAR run remained 31/32 because the runner's installed provider returned native result `1`; this is an interoperability nonconfirmation, not an efficacy test.

The current render matrix contains 14 native states: nine visible default workspaces, the two collapsed preview workspaces retained for source-level evaluation, and three progressive-disclosure states. Capture still fails closed on stale targets, source/tested-commit mismatch, missing output, error sidecars, empty images, and non-1280×820 output.

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
