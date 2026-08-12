# Soltex product reaudit and rebuild contract

Snapshot: 2026-08-11  
Baseline: `main` at `9e424a47f1cf23d8a174162d3561f5b27dbb7551`  
Reference disposition: clean-room, reference only

## Verdict

Soltex has serious native engineering behind it, but the current application presents that work like a verification console. It is not yet a cohesive system utility and it does not approach the operational depth of SteelSeries GG.

The problem is product structure:

- finished, partial, preview, and empty workspaces receive equal navigation weight;
- oversized editorial serif typography makes telemetry feel like a concept site rather than a Windows tool;
- numbered navigation and repeated implementation disclaimers expose internal project structure;
- most screens are observation-heavy and manipulation-light;
- missing capabilities dominate the first-view experience;
- no shared profile, settings, permissions, background-runtime, or notification model ties the modules together;
- the current Audio page lists endpoints but does not expose Windows audio sessions or supported controls;
- Device mesh is a model, Capture is empty, Updates is unconfigured, and Remote Assist is only a trusted launcher;
- runtime evidence is strong for implemented boundaries, but evidence quality is not product completeness.

## Exact native evidence review

The exact `6466fa58971e154dc487070d4bdf873e2cdf3175` Windows and package workflows passed, and all eleven 1280×820 native captures were inspected. The shell is materially cleaner than the baseline, but the images found four remaining product-honesty defects:

- Overview was always labeled partial because an unsupported optional GPU provider was counted as a failure;
- the persistent sidebar claimed protection was active while the Security page said protection could not be confirmed;
- Device mesh and Capture placeholders still occupied prime navigation and crowded their labels;
- Updates used a confirmed-good green state even though no signed release source is configured.

The next correction removes those contradictions, keeps the optional capabilities accessible only to evidence/render paths, and shortens the Overview provider line.

## Clean-room AppControl lessons

AppControl's public help describes four user-owned surfaces: a historical Activity timeline, an app/rule inventory, configurable Alerts, and a searchable Changes log. The useful clean-room lessons for Soltex are history that answers what happened, app-centric rollups, and alerting/AI integrations that remain off until explicitly enabled. Soltex will not copy AppControl code, driver behavior, assets, wording, branding, or layouts.

## Clean-room SteelSeries lessons

Official SteelSeries material describes GG as a module hub. Engine owns devices and profiles; Sonar owns channel routing and processing; Moments owns capture, edit, and sharing. The useful lesson is not its pixels. It is that every top-level module has a clear job, direct controls, persistent state, and a settings or recovery path.

Soltex may learn from those general interaction patterns:

- product modules, not numbered project phases;
- direct manipulation before explanatory copy;
- compact persistent status;
- progressive disclosure for device lists and advanced details;
- explicit profile and state ownership;
- app-aware audio sessions and routing only through supported Windows boundaries;
- inactive modules must be visibly optional and must not consume background resources.

No SteelSeries code, binary, asset, preset, DSP behavior, private protocol, brand element, or copied layout is permitted.

## Delivery ledger

### Stage 0 — product shell and hierarchy

Implemented on the draft rebuild branch: compact module-owned navigation, deep-neutral design tokens, restrained electric-iris accent, human UI typography, bounded first-view copy, preview badges, and explicit separation of real, partial, and preview capabilities.

### Stage 1A — bounded process action

Implemented on the draft rebuild branch: single-row End task, graceful `CloseMainWindow` request first, explicit force-stop confirmation only after the grace window, and revalidation of PID, name, Windows session, and process start time. Soltex, PID 0–4, Session 0, cross-session targets, and named critical Windows/security processes are rejected. Force stop never includes descendants. This is a user-session task action, not an administrator or service manager.

Accepted on the draft branch: the bounded process-action candidate passed its exact-head Windows and package workflows, including its native process-table capture. This does not make Soltex an elevated service manager.

## Rebuild stages

### Stage 0 — product shell and hierarchy

Status: implemented by the first rebuild slice.

- replace the editorial theme with a compact native system palette and sans typography;
- group navigation by user intent;
- mark non-operational modules as previews;
- remove unsupported telemetry from the primary Overview;
- reduce repeated disclaimer copy;
- retain all existing security and consent boundaries;
- continue exact 11-state native render evidence.

### Stage 1B — Applications inventory

Accepted on the draft rebuild branch: a first-class Applications workspace reads bounded installed-software metadata from documented uninstall registry locations and sign-in entries from Run/RunOnce and Startup folders. It exposes only display name, publisher, version, scope, source, and sign-in mode. It never reads or renders uninstall commands, startup command lines, executable paths, or `Win32_Product`; it is read-only and makes no disable/removal claim.

The exact `04da86e6e38f6a1de99b5de8edd2427792f4bf36` Windows and package workflows passed. The native capture was inspected after replacing the stock white search control and equal-card summary with one compact task-first inventory surface.

### Stage 1C — local preferences and Settings

Accepted on the draft branch at `5c4a16de85b0b63bff4779769193a1e94411c77c`: Settings owns telemetry cadence, last-workspace restore, and the default Performance-detail state. Preferences are bounded non-secret JSON under the existing per-user product data root, use atomic replacement, recover to explicit defaults after invalid or oversized input, and do not install a service or enable analytics. The exact Windows/package gates passed and the native Settings capture was inspected.

### Stage 1D — privacy-bounded Activity

Accepted on exact PR head `4437db80b844dcfe33cd8265e41cb5fcf00bfd76`: one searchable timeline stores at most 120 sanitized meaningful events, defaults to session-only memory, and adds explicit 7-day/30-day retention. Shortening retention and clearing history require main-window confirmation. Invalid, oversized, expired, future, and overflow state recovers explicitly; writes are atomic and storage/deletion failures remain visible. Owner-host and hosted Windows/package gates passed; the hosted 14-state manifest was independently revalidated and Activity/Settings pixels were inspected. Owner visual acceptance remains open.

### Stage 1 — Windows control plane

Required before calling Soltex a real system manager:

- extend Audio only through supported Core Audio session observation and confirmed read-back controls;
- extend the accepted installed-application/startup inventory only through documented, path-minimizing sources;
- retain the accepted safe process action with protected-process rejection, exact confirmation, and recovery reporting;
- add service, driver, and startup health observation without generic cleanup claims;
- add notification and background-runtime policy, including a visible off switch;
- record performance cost while idle, minimized, and under navigation churn.

### Stage 2 — Audio that can be used

- enumerate active Windows audio sessions, not only endpoints;
- support documented session volume and mute controls with immediate read-back;
- show default-device and fallback-device selection through supported Windows APIs;
- design a separate, signed virtual-audio component before claiming routing;
- do not claim EQ, noise reduction, spatial processing, or per-app routing until the actual processing path is measured and verified.

### Stage 3 — enrolled device fabric

- install a small opt-in agent on each Windows or macOS device;
- use an authenticated private mesh such as Tailscale for reachability;
- enroll devices with mutual identity, capability manifests, expiry, revocation, and per-action approval;
- make the NAS a storage and log target, never the permission authority;
- keep Google Drive personal access separate from Box work access;
- provide a complete local job ledger and an emergency stop;
- keep RustDesk as visible remote hands, not the command brain.

### Stage 4 — capture, benchmark, and hardware depth

- use Windows Graphics Capture with explicit recording indication and bounded retention;
- use supported hardware encoders and measure game impact before enabling background capture;
- implement reproducible CPU, GPU, storage, memory, and network benchmark profiles;
- record ambient state, duration, temperatures when supported, score provenance, and cancellation;
- choose GPU and thermal providers explicitly; never infer sensor values.

### Stage 5 — release quality

- select and protect a production signing identity;
- sign and timestamp binaries and installer;
- validate install, repair, upgrade, rollback, and uninstall;
- complete keyboard, UI Automation, 100–200% scaling, high contrast, reduced motion, and screen-reader testing;
- complete owner visual acceptance on representative hardware.

## Completion language

Until those stages have evidence, describe Soltex as a Windows-native personal system workspace with implemented monitoring, security companion, update planning, endpoint observation, and consent-first RustDesk handoff.

Do not call it a SteelSeries GG replacement, production antivirus, production remote-management platform, audio router or DSP suite, recorder, complete task manager, or production update service.
