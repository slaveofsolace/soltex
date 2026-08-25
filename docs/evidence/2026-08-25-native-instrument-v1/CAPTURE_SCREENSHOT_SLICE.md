# Capture screenshot slice

Date: 2026-08-25
Branch: `sol/soltex-native-instrument-v1`
Base commit: `26527fc0cff2baaefe1c5cd71c7ee822cea647db`

## Implemented source

- Versioned capture profile, replay hard bounds, source/audio/encoder/output
  enums, session lifecycle, capability snapshot, clip manifest, and local
  storage plan in `Soltex.Capture`.
- Explicit Windows display/window selection through `GraphicsCapturePicker`.
- One-frame Windows Graphics Capture pool backed by a narrow managed D3D11/DXGI
  adapter.
- PNG encoding through Windows Imaging, capped at 16,384 pixels per axis,
  35,389,440 total pixels, and 256 MiB.
- Unique `CreateNew` temporary output, exclusive read/write handle, exact length
  check, SHA-256 through the same handle, atomic no-overwrite promotion, and
  exact-temporary cleanup.
- Cancellation, picker cancellation, source close, frame timeout, device loss,
  protected/unsupported dimensions, consent, and storage failure states.
- Late frame callbacks lose ownership after cancellation or timeout, destination
  creation is inside the Capture storage error boundary, and resized surfaces
  must match the reported frame dimensions before encoding.
- A quiet Capture workspace with one enabled capability: `Take screenshot`.
  Recording and instant replay are concise unavailable states rather than inert
  controls.
- Activity receives only `Screenshot saved.` or a generic failure; it receives
  no source name, file name, path, pixel data, or digest.

## Dependency admission

The direct Vortice.Direct3D11 3.8.3 package and its five resolved managed
dependencies were preserved outside the repository, inventoried without
execution, hashed, and recorded in the Resource Pilfer candidate registry.
`THIRD_PARTY_NOTICES.md` contains the required MIT notices. Static package
inspection is not malware clearance.

Resolved dependency archives:

- Vortice.Direct3D11 3.8.3 — `1df046bdcf739b9fd368f0046f82645b777ebc5e6ef67b9f47eb661cb0001064`
- Vortice.DXGI 3.8.3 — `334b3eae1c9b2dfaf609d58c2938d41240658044e14cf5fc030ff5f190f3903c`
- Vortice.DirectX 3.8.3 — `d9d67d3be6226411fba29f7dd87baffe824dbf048f39c1878879ed1cea9e7c13`
- Vortice.Mathematics 2.1.0 — `15faefe786bc5bb8a2e0fe1e73fae7038f1aa2204ff711c3581944cd77ffadae`
- SharpGen.Runtime 2.4.2-beta — `5bdcebee0bdc3c15dc6e9b73f82faa98575ea749b92d6ae30106feb44bd23f18`
- SharpGen.Runtime.COM 2.4.2-beta — `32a81aec51f9ffee3376a29db7f9a2447e250cf1cccd352ea02fc4dc49fd046c`

## Focused verification

Commands:

```text
C:\Users\suhai\Documents\SOLTOO~1\.dotnet\dotnet.exe build tests\Soltex.Capture.Tests\Soltex.Capture.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false
C:\Users\suhai\Documents\SOLTOO~1\.dotnet\dotnet.exe run --project tests\Soltex.Capture.Tests\Soltex.Capture.Tests.csproj -c Release --no-build
```

Result: zero warnings, zero errors; 16/16 tests passed in 72.1 ms.

The app-control build uses the same WPF source as a library-shaped verification
target. That keeps executable WinRT registration and runtime payload out of the
test host while retaining generated XAML, resources, view code, and policy
integration. The test host copies only the managed projection metadata required
to load the native-shell assembly; it does not activate or package the Windows
App SDK runtime.

```text
C:\Users\suhai\Documents\SOLTOO~1\.dotnet\dotnet.exe build tests\Soltex.App.Tests\Soltex.App.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false
C:\Users\suhai\Documents\SOLTOO~1\.dotnet\dotnet.exe run --project tests\Soltex.App.Tests\Soltex.App.Tests.csproj -c Release --no-build
```

Result: zero warnings, zero errors; 55/55 tests passed in 1,633.3 ms. The
Capture-specific app test proves that the screenshot action becomes enabled only
when the capability is available, forwards the pointer choice, locks conflicting
controls while the picker is active, recovers after cancellation, and renders a
non-empty WPF surface. A separate quiet-header contract proves that redundant
workspace-level status labels remain hidden on Overview, Performance,
Applications, Devices, Audio, Activity, Settings, and Capture.

## Visual evidence

The library-shaped WPF test host rendered the same token-driven `CaptureView` at
1028 by 768. Direct inspection confirmed a single clear screenshot action, no
floating footer or engineering telemetry, semantic foregrounds in every theme,
concise local-privacy context, and an honest unavailable state for recording.

- `artifacts/native-instrument-v1/capture/capture-accepted-dark.png` —
  `2e14d3efbdce3df76e06e0479a227a705fa24812ee6119716b2d2cd913030e2c`
- `artifacts/native-instrument-v1/capture/capture-accepted-light.png` —
  `caf997f7b0beb0ea03a57a9ceb06bc791d563652ffc358fbe693409dd355951d`
- `artifacts/native-instrument-v1/capture/capture-accepted-high-contrast.png` —
  `f5f9ba9ee4762c00d17cf82bddc5f70985db5ed1f7e3d8106040a8b3ebcd3258`

The second deletion-first pass removed the repeated instruction, floating
capability pill, and split privacy copy. Final inspected artifacts:

- `artifacts/native-instrument-v1/capture/capture-quiet-final-dark.png`
  - SHA-256 `4e5b08078fec56d97b11fbf3618f9aae89058aa9ee3c866893cff9fa8a26e824`
- `artifacts/native-instrument-v1/capture/capture-quiet-final-light.png`
  - SHA-256 `a9bc4cf8e98c9b6baa1a958b190a35596d59b5216bd6c8173bb56983fd1a2b42`
- `artifacts/native-instrument-v1/capture/capture-quiet-final-high-contrast.png`
  - SHA-256 `10ec2d142ff34e8259feda352ca493aa1dc6f2edf038cb21a890e3223f41abde`

These artifacts prove the isolated WPF surface only. They do not prove the
packaged native window, Windows picker, live pixels, DPI behavior, Narrator, or
owner acceptance.

## Security diff review

Final sealed Codex Security diff scan
`2c7d8901-d56c-470d-8510-1e336eeac4ab` reviewed the Capture screenshot slice,
app-wide quiet-header cleanup, reliability hardening, tests, notices,
documentation, and ledger in frozen snapshot
`codex-security-snapshot/v1:sha256:fea4b6bafd7cfcc183b3d8ce7c48ea49b3b0b9223f4141ba8da32f7bacaf1bba`.
It completed with zero reportable findings and partial coverage.

- Findings JSON SHA-256:
  `094c8a9177a2064b988ed7d83adf075ef70ce1591ff1ce9a609f986e6a2a1edc`
- Coverage JSON SHA-256:
  `37850e2c17e261f4a450a0a0ce73d7402a30f1617e69633017f7bc07b838af31`
- Report:
  `C:\Users\suhai\AppData\Local\Temp\codex-security-scans-kYB0mf\soltex-native-instrument-v1\26527fc0cff2baaefe1c5cd71c7ee822cea647db_20260825T213653Z_oo67qkjh\report.md`

Partial coverage explicitly defers the production self-contained executable,
live picker and real pixels, live cancellation/protected-content/device-loss and
WinRT callback interleavings, directory-handle identity and reparse resistance,
package and signed-release admission, accessibility, physical-device behavior,
and owner acceptance. These remain engineering or release verification gates,
not vulnerability claims.

## Open blocker and nonclaims

The Release WPF host build compiled all referenced projects, including
`Soltex.Capture`, then produced no further output for five minutes in the
Windows App SDK self-contained manifest phase. A repository-external direct
runner recorded exact PID `113836`, stopped only that process tree at its
five-minute ceiling, and wrote:

`artifacts/native-instrument-v1/capture/app-build-direct-20260825-1535.log`

This is a current executable app-host build blocker, not a Capture-test failure.
No current claim is made for a live Windows picker, successful real
display/window pixels, picker cancellation, protected content, packaged-shell
render quality, package inclusion, encoder behavior, recording, audio capture,
instant replay, soak behavior, accessibility, or owner acceptance.

Exact resume step: diagnose or replace the self-contained Windows App SDK
manifest path without weakening AppWindow/Mica runtime registration; then run
the app-control suite, Capture dark/light/high-contrast renders, and one
owner-controlled display/window screenshot plus cancellation proof.
