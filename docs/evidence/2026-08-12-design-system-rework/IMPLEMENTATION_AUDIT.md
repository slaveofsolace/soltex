# Quiet Instrument Deck implementation audit

Date: 2026-08-12

Baseline: `sol/soltex-product-rebuild` at `1facf373ca4c27077818908e8e5498eb453e1275`

Implemented source: `d3429777c7baff67f08f1e78cc5540d71f58da03`

Human acceptance: **PENDING**

## Cold-eye baseline

The complete 16-state native baseline was inspected before source-led changes.
The working data tables and live provider state were credible, but the product
repeated the same eyebrow/title/subtitle/card grammar across modules. Security,
Remote Assist, Updates, Settings, and expanded Performance behaved like long
documents: persistent explanations competed with controls and required page
scrolling. Violet selection fills, uniform radii, a gradient hero, micro-mono
labels, and equal-weight dark cards produced a familiar generated-dashboard look.

## Implemented correction

- Replaced electric iris/violet with glacier blue and a leading selection rail.
- Removed the diagonal hero gradient and flattened the surface hierarchy.
- Reduced radii, padding, title scale, data-grid padding, and reflexive mono use.
- Added shared command-shelf, workbench, local-tab, and icon-button styles.
- Removed page eyebrows from every product and preview workspace.
- Retired eight page-level scroll viewers. The only remaining viewer is inside
  Audio device detail, where the bounded list owns its own scrolling.
- Replaced the Settings document with General, Window, Activity, and Boundaries
  categories; one category is visible at a time.
- Replaced expanding Performance detail with mutually exclusive Live overview
  and System detail work areas.
- Replaced expanding Audio device detail with mutually exclusive Mixer and Audio
  device work areas; five real app-volume sliders remain visible in the default
  captured state.
- Rebuilt Security as a posture strip, provider band, scan command deck, bounded
  Windows-event table, quarantine table, and local activity pane.
- Rebuilt Updates as Prepared preview, Planning journal, and Recovery modes.
- Rebuilt Remote Assist as a fixed client/session master-detail workbench.
- Converted Applications to a true local mode strip over the virtualized table.
- Rebuilt the hidden Device Mesh and Capture previews around concise, honest
  non-operational boundaries.

## Native endpoint review

All states below rendered successfully at 1280 by 820 from exact commit
`d3429777c7baff67f08f1e78cc5540d71f58da03` and were directly inspected:

| State | Result |
|---|---|
| Overview | No page scroll; posture, CPU, system signals, and network visible |
| Performance / System detail | Live deck and process detail replace one another cleanly |
| Applications / Services | Local tabs, search, and virtualized table remain visible |
| Activity | Search, deletion command, bounded timeline, and retention state visible |
| Settings | One bounded category, no long settings document |
| Device Mesh preview | Local-only state and capability policy fit without scrolling |
| Audio / Devices / More | Sliders visible by default; device detail scrolls internally |
| Capture preview | Concise non-installed state |
| Security / Windows events | Common scan path visible; event disclosure stays in work area |
| Remote Assist | Client and peer controls share one fixed master-detail deck |
| Updates | Preview/journal/recovery local modes, no persistent boundary essay |

Agent cold-eye inspection found no gross clipping, hidden common-path command, or
obvious default-view breach of the 75-word explanatory-copy budget. The exact
matrix contains 16 states, no error sidecars, and manifest SHA-256
`13BFBB211675118DDB558FE906F12D63BCAC8A436B1E638BE2D70F46EB682EDE`.
This is not owner acceptance or an accessibility conformance result.

External evidence root:

```text
<local-evidence-root>\2026\08\12\soltex-product-rebuild\quiet-instrument-deck-exact-d342977-20260812-031629
```

## Verification completed on the frozen source

- design-token guard: passed across 10 XAML files;
- identity policy: passed across 195 tracked text files;
- Release solution build: 0 warnings, 0 errors;
- Security companion: 31/31;
- supply chain: 18/18;
- hardening: 12/12;
- update planner: 17/17;
- Device Fabric: 24/24;
- monitoring: 16/16;
- audio: 21/21;
- application/control/render: 29/29.

The opt-in EICAR interoperability check was not rerun for a presentation-only
change. No security-protection or detection-efficacy claim depends on this UI
slice.

## Installed-copy recovery

The user-reported `Soltex.Security.QuarantineStore` disposed-object dialog came
from the installed August 5 executable at commit `8a7b4a76`, which predates the
shutdown correction at `4207ecb`. Exact source `d342977` was published and
installed in place as the per-user `1.0.1` package. The installed executable
matches the published executable byte for byte, and three independent Security
initialize/render/shutdown cycles exited `0` without an error sidecar or
remaining process. Existing authenticated security state matched before and
immediately after the upgrade. See [`INSTALL_RECOVERY.md`](INSTALL_RECOVERY.md).

## Remaining visual gates

- owner `KEEP`, `REVISE`, or `REJECT` decision;
- keyboard-only traversal and focus restoration;
- Narrator/UI Automation review;
- high contrast and reduced motion;
- 100%, 125%, 150%, and 200% Windows scaling;
- 1366×768, 1440p, and 4K representative windows;
- busy/error/recovery/confirmation state capture where the current matrix shows
  only default or populated states.

## Nonclaims

- Native captures and green tests do not grant owner visual acceptance.
- The pass does not establish WCAG conformance, screen-reader quality, or every
  display configuration.
- Reference study does not establish parity with SteelSeries GG, AppControl,
  NZXT CAM, Zen Browser, RustDesk, Defender, or Malwarebytes.
- This work changes presentation and local disclosure; it does not create an
  antivirus engine, remote transport, updater activation path, benchmark engine,
  capture pipeline, device agent, or cloud connector.
