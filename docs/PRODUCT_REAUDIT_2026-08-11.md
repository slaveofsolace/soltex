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

### Stage 1 — Windows control plane

Required before calling Soltex a real system manager:

- add a central Settings and permissions workspace with durable per-user state;
- add installed-application and startup inventory using documented registry and startup locations;
- add safe process actions with protected-process rejection, exact confirmation, and recovery reporting;
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
