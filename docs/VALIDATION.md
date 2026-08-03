# Validation record

Environment:

- Windows build 10.0.26200, x64.
- Project-local .NET SDK 10.0.302.
- .NET Windows Desktop runtime 10.0.10.
- Windows Security Center reported `Good` during the current default test runs.
- Defender reported `AMRunningMode=Normal` during the current run.

Commands executed for the last pre-correction baseline:

```powershell
.\.dotnet\dotnet.exe build .\WaveSlate.sln --configuration Release
.\.dotnet\dotnet.exe run --project .\tests\WaveSlate.Security.Tests\WaveSlate.Security.Tests.csproj --configuration Release --no-build
```

Deferred post-baseline command:

```powershell
.\eng\verify.ps1 -RunEicar
```

Deferred current-source native renders:

```powershell
.\.dotnet\dotnet.exe run --project .\src\WaveSlate.App\WaveSlate.App.csproj --configuration Release --no-build -- --render-smoke .\artifacts\visual\security-monitoring-v1.png
.\.dotnet\dotnet.exe run --project .\src\WaveSlate.App\WaveSlate.App.csproj --configuration Release --no-build -- --render-smoke .\artifacts\visual\remote-assist-v1.png --panel remote
```

Observed results:

- Build: 0 warnings, 0 errors.
- Hash/path/state/quarantine/manifest/audit tests: passed.
- Authenticode trusted Microsoft .NET host and rejected unsigned development assembly: passed.
- Benign AMSI: passed.
- Defender/WSC bounded live status: passed.
- WSC callback registration and disposal: passed.
- Protection monitor simulated outage/backoff/recovery and shutdown-race regression: passed.
- Protection monitor prompt-refresh-after-multiple-polls regression: added after static inspection; current-source execution pending command-startup recovery.
- Defender child-process output overflow regression: added after replacing post-read truncation with a bounded byte-stream reader; current-source execution pending command-startup recovery.
- Provider-neutral health precedence regression: added to prove explicit WSC warning states cannot be overwritten by Defender detail flags and passive Defender cannot substitute for unknown aggregate health; current-source execution pending command-startup recovery.
- Native import policy regression: added to prove the security assembly resolves its AMSI, WSC, Authenticode, DPAPI, and kernel P/Invokes only from System32; current-source execution pending command-startup recovery.
- PowerShell module provenance regression: added to prove fixed security scripts import Defender, Diagnostics, and Utility manifests by direct `$PSHOME` paths and module-qualify every invoked security cmdlet; current-source execution pending command-startup recovery.
- Protection fault-isolation regression: added to prove a nonfatal provider exception becomes a redacted degraded observation, recovery remains possible, and a failed update subscriber cannot stop later observations; current-source execution pending command-startup recovery.
- PowerShell child-boundary regression: added to prove the child starts in its executable directory and raw stderr containing a private path is replaced by a bounded exit-code diagnostic before reaching health/audit output; current-source execution pending command-startup recovery.
- Defender event-envelope regression: added to prove JSON returning more events than requested is rejected. The fixed script now treats only the no-match error as an empty result and propagates other provider/access failures; current-source execution pending command-startup recovery.
- Remote Assist peer-ID regression: added to prove spaces, quoting, shell metacharacters, and argument-like input cannot cross the peer-ID value boundary; current-source execution pending command-startup recovery.
- Remote Assist launch-plan regression: added to prove sharing has no arguments, control has exactly `--connect` plus one peer ID, `UseShellExecute` is false, unattended/elevation/service controls are absent, and an approved executable is blocked after its bytes change; current-source execution pending command-startup recovery.
- Remote Assist discovery regression: added to prove automatic lookup is bounded to at most two Program Files candidates and other executable names are rejected; current-source execution pending command-startup recovery.
- Current XAML received a direct structural check: tag stack balanced, 41 `x:Name` values were unique, and every new Remote Assist code-behind control reference resolved. This is static evidence only, not a WPF compile or render result.
- Render-smoke now has a fixed `--panel` selector so Security and Remote Assist can be captured independently without opening a session. The Remote Assist capture command has not run.
- Defender Operational query and privacy-redacting parser: passed.
- In-memory harmless EICAR marker: blocked as expected on the prior baseline; the current expanded opt-in run is pending because the PowerShell command path times out during startup.
- Last executed default suite: 16/16 passed before the latest source-only corrections. Current source defines 27 default tests: eight security-focused regressions and three Remote Assist boundary regressions are new and unexecuted. The required expanded gate is 28/28 when the opt-in in-memory EICAR interoperability test is included. Both current-source results remain open until command startup responds or the supplied manual validation command returns evidence.
- Latest measured timings: callback registration 5.4 ms; Defender health 607.4 ms; 16-event query 422.0 ms; WSC mean 0.336 ms across 250 reads; AMSI 4 KiB mean 1.033 ms across 32 reads.
- Native WPF health-and-event render: passed; see `artifacts/visual/security-monitoring-v1.png`.

These checks establish build/runtime integration. They do not establish malware detection rate, WCAG conformance, visual acceptance by a human owner, driver readiness, or production deployment approval.
