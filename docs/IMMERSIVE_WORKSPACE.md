# Immersive workspace

Snapshot: 2026-08-04
Scope: first real Home, Monitoring, and Devices slice

## Visual direction

Soltex keeps the established warm graphite, parchment, coral, muted green, amber, and red palette. Georgia is reserved for large editorial headings and metrics; Segoe UI Variable carries normal interface copy; Cascadia Mono is limited to provenance, state, and evidence labels. The layout uses a persistent compact sidebar, generous negative space, asymmetric hero compositions, and quiet bordered surfaces instead of a generic glass/neon dashboard.

The interaction direction is informed by the calm workspace, compact-navigation, and focus principles publicly described by [Zen Browser](https://zen-browser.app/). No Zen source, asset, trademark, icon, screenshot, layout measurement, or proprietary interface has been copied. RustDesk remains a separately installed external client under the clean-room boundary in `REMOTE_ASSIST.md`; no RustDesk UI or AGPL runtime is embedded.

### Reference disposition

Resource Pilfer disposition: **REFERENCE ONLY**. The official Zen product page was checked on 2026-08-04 for general principles such as calm focus, compact navigation, workspaces, and an explicit balance among beauty, performance, and privacy. The official RustDesk repository and license were checked on the same date to preserve the external-client and AGPL-3.0 boundary. No third-party repository, archive, executable, font, icon, image, design token, or UI payload was acquired for this visual slice. The retained provenance decision is in `evidence/2026-08-04-immersive-workspace/RESOURCE_PROVENANCE.md`.

## Shared design system

`src/Soltex.App/Themes/SoltexTheme.xaml` owns application-wide color, typography, card, button, progress, slider, focus, table, and scrollbar resources. `Sparkline` is a small native WPF chart primitive that accepts finite percentage samples, clamps them to zero through 100, retains no data of its own, and draws a truthful flat line when only one sample exists.

Panel transitions use a 160 ms opacity ease only when `SystemParameters.ClientAreaAnimation` is enabled. With Windows client-area animation disabled, panel changes are immediate. Keyboard focus is visible on action buttons and the shared slider. This is an implemented reduced-motion/focus behavior, not a WCAG or assistive-technology conformance claim.

## Page behavior

### Home

Home is now the default surface. It shows:

- aggregate CPU from the current bounded sample and its copied 48-sample history;
- physical-memory use;
- the first ready fixed volume and its used percentage;
- the highest CPU row in the bounded process result;
- sanitized local machine/OS identity;
- explicit GPU, network, and remote-peer gaps.

The machine profile is `NotEnrolled`. Home does not imply that the local observation is a trusted agent identity.

### Monitoring

Monitoring shows CPU and memory histories, up to eight fixed volumes, and up to 32 process rows. Process rows contain only sanitized name, PID, bounded CPU percentage, working set, and thread count. Executable paths are not requested or rendered. Provenance, capture time, provider duration, inaccessible-process count, and limitations remain visible.

Supported sources are:

- `GetSystemTimes` for aggregate CPU timing;
- `GlobalMemoryStatusEx` for physical memory;
- `System.Diagnostics.Process` for the bounded process summary;
- `DriveInfo` for ready fixed-volume capacity.

GPU load/clocks/temperature/fans and network throughput are not sampled. They remain labeled unavailable rather than being inferred from unrelated counters.

### Devices

Devices renders one sanitized local observation from `Environment.MachineName` and `RuntimeInformation`. It explicitly reports no enrolled agent or peer. The six capability cards are the existing policy model only:

- three read-only observations under device-local policy;
- two Defender requests requiring visible per-job local consent;
- one visible RustDesk handoff requiring visible per-job local consent and an external client.

The page cannot discover a NAS, enroll another PC or Mac, authenticate a manifest, send a job, control a remote device, or prove consent. Its Remote Assist button navigates to the separately verified external-client flow.

## State and recovery contract

The workspace distinguishes:

- **waiting:** no sample has completed yet;
- **current:** every signal required by the snapshot is available;
- **partial:** supported readings completed while declared providers/signals remain absent;
- **stale:** the last copied values remain visible for at most two failed retries;
- **unavailable:** no prior sample exists or failures exceeded the stale window;
- **recovered:** the next successful sample replaces retained values and writes one local activity notice.

Sampling is sequential and non-overlapping. Each provider window is 100 ms through two seconds; the UI uses 300 ms, then waits two seconds. Retry delay is bounded at ten seconds. Closing the WPF window cancels and awaits the loop before disposing owned runtimes.

## Bounds and measured evidence

The monitoring provider observes at most 2,048 process handles per pass, returns at most 32 process rows and eight fixed volumes, and limits process names to 80 control-free characters. Local-device fields are limited to 96 control-free characters. Histories accept only finite values, copy every exposed snapshot, retain at most 120 values by type and 48/72 in the current pages, and evict oldest values first.

The exact current Windows build, focused suites, native render commands, measured durations, artifact identity, and limitations are maintained in `VALIDATION.md`. Hosted-runner timings are diagnostics, not performance guarantees for the owner's PC.

The frozen six-panel artifact received a Human Eye cold-eye review for first-impression hierarchy, coherence, readability, clipping, proportions, and trust. The verdict for this bounded slice is **KEEP**, with multi-viewport, scaling, keyboard, screen-reader, contrast, and owner acceptance still open. Because implementation context was already known, this was not a source-naive study. See `evidence/2026-08-04-immersive-workspace/VISUAL_REVIEW.md`.

## Honest expansion sequence

Later areas stay separate so permissions and evidence do not bleed together:

1. read-only Applications inventory and bounded process actions with confirmation;
2. supported audio endpoint/session inventory before any routing or EQ claim;
3. privacy adapters that expose exact provider/action provenance;
4. local search and NAS indexing with explicit roots, quotas, and cancellation;
5. personal Google Drive OAuth and separately governed work Box OAuth, credentials, indices, audit streams, and transfer rules;
6. clips capability probing before Windows Graphics Capture/Media Foundation implementation;
7. signed, replay-resistant Device Fabric envelopes and receipts before transport;
8. private-mesh status only after a separately installed Tailscale boundary is defined.

No later card should display invented data or an enabled action merely because its page exists.

## Nonclaims

This slice is not owner visual acceptance, accessibility conformance, production monitoring, benchmark comparability, GPU/network monitoring, device enrollment, a remote executor, autonomous correction, an embedded RustDesk client, an antivirus engine, or evidence that future Audio, Applications, Privacy, Clips, NAS, Drive, or Box systems work.
