# Implementation status

Snapshot: 2026-08-04
Product: **Soltex**  
Repository: `slaveofsolace/soltex` (private)  
Current implementation identifiers: `Soltex.*`

## Evidence vocabulary

- **Windows-verified** means the stated source compiled and the stated behavior was exercised on the documented Windows host with retained results.
- **Current source, verification pending** means implementation exists but the latest source has not completed the stated gate.
- **Designed** means an architecture or contract exists without a working end-to-end feature claim.
- **Not implemented** means a page, model, or document may exist but the real pipeline does not.

A passing narrow test proves only that boundary. It does not establish production readiness, security efficacy, accessibility conformance, owner visual acceptance, installer safety, update-service availability, or resilience against a fully compromised user account.

## Windows-verified current branch baseline

Immersive-workspace implementation commit `2a0699b2ca77b30fa636279b1d5ecab603a8bde9` was exercised by GitHub Actions run `30925606488`, job `92046999983`, on a hosted Windows runner. This is the frozen implementation identity; later documentation-only commits do not replace it as the source of the runtime evidence below.

| Gate | Result | Scope |
|---|---:|---|
| Identity policy | Passed | Current-source naming and reasoned compatibility/historical allowlist policy |
| Release build | 0 warnings, 0 errors; 41.59 s | Entire 13-project `Soltex.sln`: six production and seven focused test projects |
| Existing focused suite | 31/31 passed | Security companion, identity/root compatibility, monitoring boundaries, Remote Assist regressions |
| Supply-chain suite | 18/18 passed | Publisher policy, signed release, sequence state including cross-process lock, bounded ZIP staging |
| Hostile hardening suite | 12/12 passed | Immutable publisher snapshot, authenticated-state recovery, bounded ZIP preflight, pinned workflow policy |
| Update-planner suite | 17/17 passed; 2,895.6 ms | Descriptor quorum/expiry, trust rotation, bounded acquisition cleanup, journal recovery, exact confirmation |
| Device Fabric suite | 24/24 passed; 47.7 ms | Exact catalog, target-manifest binding, local consent, immutable snapshots, bounded local observation |
| Monitoring suite | 13/13 passed; 765.9 ms | CPU/memory math, process/path bounds, cancellation, immutable history, live bounded capture and measurement |
| WPF control/render suite | 5/5 passed; 1,704.3 ms | Shared controls, sparkline pixels, real Home/Monitoring bindings, Devices contract, layout bounds |
| Opt-in EICAR interoperability | 31/32 | Hosted AMSI provider returned native result `1`; owner-host evidence remains pending |
| Home native render | Passed | 1044×788 render-smoke output created |
| Monitoring native render | Passed | 1044×788 render-smoke output created |
| Devices native render | Passed | 1044×788 render-smoke output created |
| Security native render | Passed | 1044×788 render-smoke output created |
| Remote Assist native render | Passed | 1044×788 render-smoke output created |
| Updates native render | Passed | 1044×788 render-smoke output created |
| Render artifact check | Passed | All six PNGs present; no `*.error.txt` output |

Retained workflow evidence:

- run: `30925606488`;
- job: `92046999983`;
- artifact: `soltex-windows-evidence-30925606488-1`;
- artifact ID: `8899014386`;
- uploaded artifact ZIP size: 626,093 bytes;
- GitHub-recorded artifact ZIP SHA-256: `71905016B3A67F8CE340D90C2E404DEBFB185C3DAE8A973F21B80F1B8A94515`;
- independently downloaded artifact ZIP SHA-256: `71905016B3A67F8CE340D90C2E404DEBFB185C3DAE8A973F21B80F1B8A94515`;
- retention configured by the workflow: 30 days.

The hosted Windows image did not expose a usable live `wscapi.dll` boundary. The provider-neutral health test therefore proved bounded failure/fallback behavior and an `Unknown` state, not successful provider inventory on that host. Native rendering proves that all six panels initialize and capture under the Soltex identity. A current-capture Human Eye review found no gross hierarchy, clipping, or identity blocker after the final corrections. That review was not source-naive and does not grant owner visual acceptance. Broader viewport/scaling, accessibility, keyboard/screen-reader, and owner-review coverage remain pending.

The identity wave also adds a fail-closed local-data-root resolver. Fresh profiles use the canonical root; a sole existing compatible root remains in place; dual roots, file collisions, and reparse paths are rejected. No automatic state move is claimed. See `NAMING_AND_COMPATIBILITY.md` and the retained identity-migration security review.

## Implemented: Security companion

The current Windows application remains an unelevated, user-mode security companion. It implements:

- Windows Security Center aggregate antivirus-health observation with bounded fallback;
- Defender status, mode, scan/update request, and bounded Operational-event integration through supported Windows and PowerShell boundaries;
- AMSI inspection for bounded content that Soltex itself ingests;
- SHA-256 assessment and exact-hash allow decisions;
- Authenticode trust verification;
- detached RSA-PSS/SHA-256 integrity-manifest verification;
- authenticated quarantine and HMAC-chained audit state;
- bounded import-folder observation;
- cancellation, byte limits, timeouts, path redaction, module pinning, System32-only native imports, and monitoring fault isolation covered by focused regression checks.

It does not disable Defender, add exclusions, change another provider's registration, automate Windows Security settings, or register Soltex as antivirus software.

## Implemented: publisher authorization primitive

`PublisherPolicy`, `AuthenticodePublisherVerifier`, and the extended `AuthenticodeVerifier` implement a bounded authorization decision for one signed Windows file:

1. normalize an existing non-reparse file;
2. compute SHA-256 before verification;
3. ask `WinVerifyTrust` to verify embedded signature index `0`;
4. request the secondary-signature count and reject any secondary signature;
5. require successful Windows trust evaluation;
6. read the unique embedded signer certificate;
7. require the explicit code-signing enhanced-key-usage OID;
8. require an approved exact X.500 subject-name DER SHA-256 and SubjectPublicKeyInfo SHA-256 pair;
9. compute SHA-256 again and reject a changed file.

A certificate thumbprint alone is deliberately not the authorization identity. The observed `dwVerifiedSigIndex` value is retained as evidence but is not relied on for authorization because the hosted WinTrust implementation did not consistently report it after a successful explicit-index-0 request.

**Production gap:** no Soltex production signer or pin is configured. The Windows-verified test derives a temporary policy from the trusted Microsoft-signed `.NET` host already present on the runner. Selecting the Soltex release certificate, protecting its private key, recording the approved subject/SPKI values, and defining rotation/revocation policy remain future release-engineering work.

## Implemented: signed release verification and local anti-rollback state

`SignedReleaseManifestVerifier` implements strict schema-2 `Soltex` manifests:

- detached RSA-PSS/SHA-256 verification over the exact manifest bytes;
- case-sensitive JSON with unknown fields rejected;
- bounded manifest, signature, file, and error counts;
- exact product identifier, positive sequence, bounded channel/version, and explicit UTC publication time;
- canonical Windows-relative file paths with forward slashes only;
- rejection of rooted paths, traversal aliases, backslashes, alternate-data-stream syntax, controls, trailing spaces/dots, reserved device names, excessive path length/depth, and case-insensitive duplicates;
- content-root and intermediate-path reparse rejection;
- per-file length and SHA-256 validation;
- normalized successful file evidence;
- an internal success-result constructor so callers cannot ordinarily manufacture a successful verification object.

`ReleaseSequenceStore` persists the highest accepted sequence and signed-manifest hash per channel in authenticated, per-user state. It accepts first release, upgrade, and exact idempotent replay; it rejects a lower sequence and a different manifest reusing an accepted sequence. State schema, timestamp, channel, version, hash, duplicate, and reparse boundaries are validated.

Each read-modify-write operation is additionally serialized through a state-directory lock file opened with `FileShare.None`. Lock acquisition retries for a bounded ten seconds, honors cancellation, rejects a reparse-point lock file, and releases automatically if a process exits. The regression suite holds the lock from a separate file handle, verifies cancellation, releases it, and confirms the same store instance recovers.

**Nonclaims:** this is not a TPM- or hardware-backed monotonic counter. A fully compromised same-user account can deny service by holding or manipulating user-owned state and may be able to restore an older authenticated state file together with matching DPAPI-protected key material. The verifier does not fetch updates, establish transport authenticity, select a production public key, install files, activate versions, or recover a failed installation.

## Implemented: bounded ZIP staging primitive

`BoundedArchiveStager` manually iterates ZIP entries and stages regular files into a random private directory. It enforces:

- maximum archive bytes, entry count, per-entry expanded bytes, total expanded bytes, compression ratio, path length, and path depth;
- canonical Windows-safe relative paths;
- traversal, root, alternate-data-stream, control-character, trailing-space/dot, and reserved-device-name rejection;
- case-insensitive duplicate and file/directory collision rejection;
- ZIP reparse, Unix symbolic-link, and unsupported entry-type rejection;
- `FileMode.CreateNew`, bounded streaming, actual-byte ceilings, and incremental SHA-256;
- reparse checks on staging paths;
- cleanup after rejection, cancellation, and owner disposal.

The source does not call `ExtractToDirectory` for this untrusted boundary.

**Nonclaims:** staged content remains inert. It is not executed, loaded as a plugin, recursively unpacked, copied into an installation directory, or treated as approved merely because extraction succeeded. The non-installing planner now binds acquisition, staging, signed-manifest verification, and publisher evidence into a preview, but no transactional installation, activation, or installation recovery exists.

## Implemented: non-installing update planner

`Soltex.Update` provides a fail-closed planning boundary for a future Soltex release path:

- strict signed acquisition descriptors with bounded size/count fields, UTC issue/expiry, product/channel/sequence identity, exact artifact lengths and SHA-256 values, authorized origins, and RSA-PSS/SHA-256 metadata-signature quorum;
- an explicit trust policy for metadata keys, release keys, TLS SubjectPublicKeyInfo pins, and executable publisher identities;
- signed, overlap-preserving trust-policy rotation with exact replay idempotency and explicit rollback, sequence-equivocation, key-ID replacement, pin replacement, and lost-continuity rejection;
- `PinnedHttpsTransport` and `BoundedHttpsAcquirer`, which require HTTPS, pass an exact active pin set to the transport, reject unsigned redirect origins, bound redirects/headers/body/time, stream into a private reparse-free directory, enforce declared length and SHA-256, retain exclusive file handles, and clean partial acquisition on rejection or cancellation;
- `SoltexUpdatePlanner`, which composes descriptor verification, bounded acquisition, signed-manifest verification, bounded archive staging, exact manifest-to-staged-file binding, executable publisher authorization, disk-impact calculation, warning/recovery prerequisites, a deterministic plan hash, and an expiring exact confirmation phrase;
- an authenticated current/last-known-good planning journal whose untrusted detail is sanitized, whose entries are bounded, and whose recovery inspection identifies only Soltex-owned private staging tokens;
- a Soltex Updates page that reads the real authenticated journal, fails closed on authentication/read errors, exposes no release URL or raw local path, and truthfully disables planning because production release identities and a signed source are not configured.

The planner stops at `PreparedSoltexUpdate`. Disposal removes its inert private artifacts. It does not execute, install, elevate, activate, repair, uninstall, reboot, mutate Windows security, or silently clean an interrupted attempt.

The 17-case hostile suite completed in 2,895.6 ms on the current hosted runner. Individual cases ranged from 0.9 ms for exact confirmation semantics to 333.4 ms for the descriptor-quorum case. These are diagnostic wall-clock observations, not throughput or latency guarantees.

## Implemented: Remote Assist boundary

`Soltex.RemoteAssist` remains a narrow adapter for a separately installed RustDesk executable. It implements explicit selection, reparse rejection, SHA-256 and Authenticode revalidation, constrained peer IDs, a fixed shell-free `--connect` plan, local confirmation, and peer-ID-free audit events.

It does not embed or link RustDesk AGPL code, store remote passwords, enable unattended access, install services, request elevation, open listeners, hide sessions, or bypass local consent.

## Implemented: bounded local Windows monitoring

`Soltex.Monitoring` captures immutable, bounded snapshots from supported Windows interfaces: aggregate CPU timing from `GetSystemTimes`, physical memory from `GlobalMemoryStatusEx`, a sanitized process summary from `System.Diagnostics.Process`, and ready fixed-volume capacity from `DriveInfo`. It observes at most 2,048 processes, returns at most 32 rows and eight volumes, exposes no executable path, and records capture time, duration, provenance, inaccessible-process count, and limitations.

The WPF host runs one sequential 300 ms sample followed by a two-second delay. Histories return copied read-only snapshots, accept only finite percentages, and evict oldest samples at their configured 48/72 bounds. A failed sample retains confirmed values as stale for two bounded retries, then becomes unavailable; a subsequent success replaces the values and records recovery. GPU and network telemetry remain explicitly unavailable.

## Implemented: immersive local workspace

Home, Monitoring, and Devices are real default/navigation surfaces under one shared WPF resource dictionary. The visual system provides the warm graphite/coral palette, editorial/UI/mono type roles, cards, buttons, progress, slider, focus, table, scrollbar, and sparkline primitives. Panel opacity motion is enabled only when Windows client-area animation is enabled. Five WPF control/render checks cover theme resources, chart output, real Home/Monitoring data binding, bounded rows/provenance, the local unenrolled profile, and hero-width/height regressions.

Native render-smoke now targets Home, Monitoring, Devices, Security, Remote Assist, and Updates. A render pass is runtime initialization evidence only; visual acceptance, multi-scale coverage, keyboard/screen-reader review, and accessibility conformance remain separate.

## Implemented: Device Fabric Stage 1 policy and local observation

`Soltex.DeviceFabric` implements an immutable, non-executing device inventory and capability-policy model. Its exact six-capability catalog covers three read-only observations, two supported Defender requests, and one visible RustDesk handoff preparation. Every policy decision requires a structurally valid target manifest; a known capability is denied if the target did not advertise it.

Read-only observations still require device-local policy. Defender requests and the RustDesk handoff require visible, per-job local consent, and remote handoff additionally requires the external client to remain visible. The expanded 24-case suite also proves that local machine/runtime fields are bounded and sanitized and that the observation is explicitly `NotEnrolled`. The suite rejects missing target manifests, unadvertised or unknown capabilities, generic shell, arbitrary download-and-execute, hidden control, identifier/display injection, duplicate capabilities/devices, oversized inventory, and non-Windows Defender declarations.

This stage has a local profile/UI but no enrolled device agent, controller, manifest authentication, signed job envelope, replay defense, transport, listener, enrollment flow, consent authenticator, receipt, capability executor, NAS connector, or cloud connector. Its policy inputs are modeled facts supplied by a future trusted local boundary, not proof that consent or device identity occurred. It cannot perform remote correction.

## Designed or not implemented

The following remain separate work:

- production Soltex publisher identity, key custody, timestamping, pin rollout, and pin rotation;
- production release-manifest/metadata keys, TLS pins, signed trust policy, and authenticated descriptor source;
- a signed installer and deterministic uninstall;
- atomic activation, rollback, crash recovery, interrupted-update recovery, and retained installer evidence;
- optional provider-name inventory without changing Windows Security Center registration;
- provider registration, minifilter, ELAM, PPL/protected service, MVI participation, cloud reputation, detection research, certification, and efficacy claims;
- the Audio, Clips, Applications/App Control, Privacy, Device Fabric transport/execution, NAS, Drive, and isolated Box implementation waves described elsewhere.

## Current local ownership checkpoint

The implementation and closeout are isolated in `C:\Users\suhai\Documents\soltex-immersive-workspace` on branch `feat/soltex-immersive-workspace-v1`. The protected owner checkout `C:\Users\suhai\Documents\SOL Tools` was re-observed on 2026-08-04 at `main`, commit `bf2662de80992cfed761625642f084d3caaa0f04`, with extensive pre-existing legacy-named dirty and untracked work owned outside this task. It was not reset, cleaned, stashed, merged, or overwritten. No local `Soltex.exe` or `dotnet.exe` process was observed owning the task worktree at the checkpoint.

Owner-host EICAR behavior and owner visual acceptance remain unverified. Re-run the exact preflight in [`VALIDATION.md`](VALIDATION.md) before a future local mutation because checkout and process state can drift.

## Exact next implementation slice

1. Design the Device Fabric Stage 2 loopback-only signed job envelope: target binding, issuer, expiry, nonce, idempotency, policy version, replay store, cancellation, redacted receipt, and explicit approval level. Keep it non-networked and non-executing until hostile parser/state tests are green.
2. Select the production Soltex code-signing, metadata-signing, and release-manifest-signing identities; document custody/recovery; and commit only approved public subject/SPKI/key values.
3. Configure a real signed update trust policy and authenticated descriptor source, then repeat hostile transport, expiration, revocation, partial-I/O, and recovery evidence against release-candidate fixtures without installing them.
4. Specify the smallest privileged installer boundary separately from Device Fabric, including immutable input handles, exact plan binding, user confirmation, least privilege, atomic activation, rollback, repair, uninstall, reboot, and retained evidence.

Do not begin with provider registration, a driver, a service, public ingress, Defender mutations, or automatic execution of staged content.
