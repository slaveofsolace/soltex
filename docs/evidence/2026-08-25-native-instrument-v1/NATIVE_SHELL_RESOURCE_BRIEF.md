# Native shell resource brief

Date: 2026-08-25

Decision: **ADAPT** the smallest Windows App SDK component set that provides
`AppWindow`; keep Mica on the supported Windows DWM path. Do not admit the broad
aggregate package merely for convenience.

## User-visible job

Give the .NET 10 WPF shell native Windows 11 window behavior: native caption
buttons and snap layouts, a draggable integrated title region, Mica for the
long-lived window when the operating system permits it, and a solid fallback
that never blocks launch. The shell must stay quiet, legible, keyboard-safe,
and recognizably Soltex.

## Design intent

- Direction: Quiet Native Instrument.
- Type: Segoe UI Variable for interface hierarchy; Cascadia Mono only for
  measured or machine-state detail.
- Palette: graphite foundation, one Glacier interaction signal, semantic
  green/amber/red only for real state.
- Layout: one dominant workspace, compact persistent navigation, native title
  region, no card carpet or stacked glass.
- Motion: 120–200 ms functional transitions; no decorative loop; Windows
  reduced-motion preference wins.
- Reference boundary: Apple public guidance informs calm hierarchy, agency,
  simplicity, and craft. Microsoft guidance defines actual Windows behavior.
  No Apple or competitor asset, measurement, component, or trade dress enters
  the implementation.

## Runtime and delivery constraints

- Windows 11 24H2 or later, x64 only for V1.
- .NET 10 WPF remains the UI framework; no WinUI rewrite or XAML island.
- Package input must be Microsoft-owned, NuGet-signed, version-pinned, and
  restorable into the task cache on `D:`.
- Use only the AppWindow/windowing component required by this slice. AI, ML,
  Widgets, Search, DWrite, and WinUI component packages are not admitted.
- Self-contained development output is acceptable for deterministic native
  proof. Final MSIX dependency strategy is a later packaging gate.
- The app stays unelevated and performs no installer, deployment-agent, restart,
  registry-tuning, service, driver, or security-provider action in this slice.

## Screening budgets

- Incremental shipped x64 windowing payload: target at most 30 MiB; measure the
  final output rather than inferring from the multi-architecture archive.
- Cold interactive launch remains below the V1 target of 3 seconds.
- Settled idle remains below 1% CPU and 300 MiB total working set.
- Backdrop and title-region refresh must not add a recurring timer.
- Resize, theme change, DPI change, and fallback updates must be bounded and
  remain on the owning UI thread.

## Security and recovery

- Inspect package metadata, license, archive paths, active content, MSBuild
  imports, signatures, and checksums before use.
- Do not execute package-bundled helpers. Build execution is limited to the
  reviewed Microsoft MSBuild targets required to copy and register the exact
  self-contained component payload.
- AppWindow or DWM failure resolves to the ordinary WPF system title bar or a
  solid Soltex surface. No native integration failure may prevent launch.
- Detach event handlers and release windowing objects during ordinary shutdown.
- Rollback is source-level: remove the direct package reference, native adapter,
  and title-region XAML; no machine state is mutated.

## Required acceptance evidence

1. Restore and package signature logs with archive SHA-256 and inventory.
2. Focused policy/geometry/theme/fallback unit tests.
3. Zero-warning Release build and existing application suite.
4. Launched native runtime evidence for AppWindow attachment, Mica or explicit
   solid fallback, native caption buttons, snap layout, resize, theme switch,
   DPI update, and clean shutdown.
5. Render captures in light, dark, high contrast, compact, narrow, and 200%
   scaling, followed by cold-eye, anti-template, keyboard, and Narrator review.
6. Measured output-size, launch, idle CPU/working-set, resize, and disposal
   evidence.
7. MSIX install/update/repair/uninstall evidence before release admission.

## Implemented architecture checkpoint

```text
WPF MainWindow
  -> Soltex-owned capability and drag-geometry policy
  -> Soltex.NativeShell neutral adapter
       -> Windows App SDK AppWindow and AppWindowTitleBar
       -> documented DWM system-backdrop attributes
  -> system title bar plus opaque token surface on any failed capability
```

- `Soltex.NativeShell` is a non-WPF adapter. WinRT projection generation runs
  there rather than inside WPF's temporary XAML compilation.
- The WPF executable owns the signed x64 runtime payload and embedded activation
  manifest. Its unused WinRT source generator is removed before `CoreCompile`;
  application code consumes only the Soltex-owned adapter contract.
- The custom title row is collapsed unless AppWindow attachment succeeds. A
  conservative 138-DIP caption reserve remains when initial platform metrics
  report zero, preventing the drag rectangle from covering caption controls.
- Mica is requested only for Windows 11 when high contrast is off and Windows
  transparency is available. Every other state uses `CanvasBrush`; no recurring
  timer was added.
- Window, DPI, size, workspace-title, live appearance, and shutdown changes are
  bounded on the WPF dispatcher. Native session disposal happens before the
  longer application resource drain.

## Current evidence

- Adapter and host Release builds: zero warnings, zero errors.
- Focused native policy suite: 11/11 passed.
- Application regression suite: 53/53 passed.
- Design-token policy: 13 XAML files passed; raw colour remains confined to
  `Themes/Tokens.xaml`.
- Identity policy: 331 tracked text files passed with the existing reasoned
  allowlist.
- Diff-scoped security review: all seven changed source files plus their direct
  package, manifest, workflow, and evidence boundaries were reviewed; no
  reportable finding was established. Exceptional DWM-import failure, exact
  transparency-setting equivalence, and self-contained sidecar provenance remain
  explicit hardening questions rather than completion claims.
- Launched dark, light, high-contrast, and compact-150 client renders exited
  cleanly. Content-free runtime sidecars report `appWindowAttached: true`,
  `backdropApplied: true`, `backdrop: Mica`, and `chrome: AppWindow` on the
  owner-controlled Windows 11 host.
- The extracted executable manifest contains Microsoft UI Windowing activation
  declarations. The measured x64 windowing payload is 17,881,280 bytes, below
  the 30 MiB screening budget; the complete current development output is
  48,750,919 bytes.
- Evidence root:
  `artifacts/native-instrument-v1/native-shell`.

## Current nonclaims

The client render and content-free sidecar prove this controlled process reached
the AppWindow and DWM success paths. They do not capture the non-client frame,
so native caption-button appearance, maximize-hover snap layout, pointer hit
testing, live per-monitor DPI movement, or physical keyboard behavior remain
unproven. Mica transparency is not visually established by an off-screen
`RenderTargetBitmap`. Accessibility, cold launch, settled idle cost, long-run
stability, MSIX lifecycle, final security review, and human visual acceptance
also remain open.
