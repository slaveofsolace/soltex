# Implementation status

Snapshot: 2026-08-04
Product: **Soltex**  
Repository: `slaveofsolace/soltex` (private)  
Current implementation identifiers: `WaveSlate.*`

## Evidence vocabulary

- **Windows-verified** means the stated source compiled and the stated behavior was exercised on the documented Windows host with retained results.
- **Current source, verification pending** means implementation exists but the latest source has not completed the stated gate.
- **Designed** means an architecture or contract exists without a working end-to-end feature claim.
- **Not implemented** means a page, model, or document may exist but the real pipeline does not.

A passing narrow test proves only that boundary. It does not establish production readiness, security efficacy, accessibility conformance, owner visual acceptance, installer safety, update-service availability, or resilience against a fully compromised user account.

## Windows-verified current branch baseline

Implementation commit `8ca28f8aa1f50de929787fe1c1cbd23b96b3f6e9` was exercised in pull-request merge preview `035de520a6ea346b9aeb08270fa4f72af86d59c0` by GitHub Actions run `30918120029` on Windows Server 2025 (`10.0.26100`, image `windows-2025-vs2026` `20260728.188.1`) with .NET SDK `10.0.302`.

| Gate | Result | Scope |
|---|---:|---|
| Release build | 0 warnings, 0 errors | Entire `WaveSlate.sln`, including Security, Remote Assist, Update, Device Fabric, and all focused test projects |
| Existing focused suite | 27/27 passed | Security companion, monitoring boundaries, Remote Assist regressions |
| Supply-chain suite | 18/18 passed | Publisher policy, signed release, sequence state including cross-process lock, bounded ZIP staging |
| Hostile hardening suite | 12/12 passed | Immutable publisher snapshot, authenticated-state recovery, bounded ZIP preflight, pinned workflow policy |
| Update-planner suite | 17/17 passed | Descriptor quorum/expiry, trust rotation, bounded acquisition cleanup, journal recovery, exact confirmation |
| Device Fabric policy suite | 20/20 passed | Exact catalog, target-manifest binding, local consent, injection/bounds, immutable snapshots |
| Opt-in EICAR interoperability | 27/28 | Hosted AMSI provider returned native result `1`; owner-host evidence remains pending |
| Security native render | Passed | 1044×788 render-smoke output created |
| Remote Assist native render | Passed | 1044×788 render-smoke output created |
| Updates native render | Passed | 1044×788 render-smoke output created |
| Render artifact check | Passed | All three PNGs present; no `*.error.txt` output |

Retained workflow evidence:

- run: `30918120029`;
- job: `92021363535`;
- artifact: `soltex-windows-evidence-30918120029-1`;
- artifact ID: `8895955309`;
- uploaded artifact ZIP size: 323,473 bytes;
- GitHub-recorded artifact ZIP SHA-256: `02C121DA84A68988B0D50B1F8CB3CC50C72D299AC3A1CA3D4C7D1C4146ACA31A`;
- retention configured by the workflow: 30 days.

The hosted Windows Server image did not expose a usable live `wscapi.dll` boundary. The provider-neutral health test therefore proved bounded failure/fallback behavior and an `Unknown` state, not successful provider inventory on that host. Native rendering proves that the Security, Remote Assist, and Updates panels initialize and capture. Pixel inspection at 1044×788 confirmed that the previously recorded Security scan-subtitle and event-detail truncation is corrected; broader viewport/scaling coverage and owner visual acceptance remain pending.

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
- cancellation, byte limits, timeouts, path redaction, module pinning, System32-only native imports, and monitoring fault isolation covered by the existing 27 checks.

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

`WaveSlate.Update` provides a fail-closed planning boundary for a future Soltex release path:

- strict signed acquisition descriptors with bounded size/count fields, UTC issue/expiry, product/channel/sequence identity, exact artifact lengths and SHA-256 values, authorized origins, and RSA-PSS/SHA-256 metadata-signature quorum;
- an explicit trust policy for metadata keys, release keys, TLS SubjectPublicKeyInfo pins, and executable publisher identities;
- signed, overlap-preserving trust-policy rotation with exact replay idempotency and explicit rollback, sequence-equivocation, key-ID replacement, pin replacement, and lost-continuity rejection;
- `PinnedHttpsTransport` and `BoundedHttpsAcquirer`, which require HTTPS, pass an exact active pin set to the transport, reject unsigned redirect origins, bound redirects/headers/body/time, stream into a private reparse-free directory, enforce declared length and SHA-256, retain exclusive file handles, and clean partial acquisition on rejection or cancellation;
- `SoltexUpdatePlanner`, which composes descriptor verification, bounded acquisition, signed-manifest verification, bounded archive staging, exact manifest-to-staged-file binding, executable publisher authorization, disk-impact calculation, warning/recovery prerequisites, a deterministic plan hash, and an expiring exact confirmation phrase;
- an authenticated current/last-known-good planning journal whose untrusted detail is sanitized, whose entries are bounded, and whose recovery inspection identifies only Soltex-owned private staging tokens;
- a Soltex Updates page that reads the real authenticated journal, fails closed on authentication/read errors, exposes no release URL or raw local path, and truthfully disables planning because production release identities and a signed source are not configured.

The planner stops at `PreparedSoltexUpdate`. Disposal removes its inert private artifacts. It does not execute, install, elevate, activate, repair, uninstall, reboot, mutate Windows security, or silently clean an interrupted attempt.

The 17-case hostile suite completed in 2,602.5 ms on one hosted runner. Individual cases ranged from 1.0 ms for exact confirmation semantics to 260.4 ms for the descriptor-quorum case. These are diagnostic wall-clock observations, not throughput or latency guarantees.

## Implemented: Remote Assist boundary

`WaveSlate.RemoteAssist` remains a narrow adapter for a separately installed RustDesk executable. It implements explicit selection, reparse rejection, SHA-256 and Authenticode revalidation, constrained peer IDs, a fixed shell-free `--connect` plan, local confirmation, and peer-ID-free audit events.

It does not embed or link RustDesk AGPL code, store remote passwords, enable unattended access, install services, request elevation, open listeners, hide sessions, or bypass local consent.

## Implemented: Device Fabric Stage 1 policy foundation

`WaveSlate.DeviceFabric` implements an immutable, non-executing device inventory and capability-policy model. Its exact six-capability catalog covers three read-only observations, two supported Defender requests, and one visible RustDesk handoff preparation. Every policy decision requires a structurally valid target manifest; a known capability is denied if the target did not advertise it.

Read-only observations still require device-local policy. Defender requests and the RustDesk handoff require visible, per-job local consent, and remote handoff additionally requires the external client to remain visible. The 20-case hostile suite rejects missing target manifests, unadvertised or unknown capabilities, generic shell, arbitrary download-and-execute, hidden control, identifier/display injection, duplicate capabilities/devices, oversized inventory, and non-Windows Defender declarations. The suite completed in 37.5 ms on one hosted runner; this is a diagnostic observation, not a latency guarantee.

This stage has no device collector, controller, manifest authentication, signed job envelope, replay defense, transport, listener, enrollment, consent authenticator, receipt, capability executor, NAS connector, cloud connector, or UI. Its policy inputs are modeled facts supplied by a future trusted local boundary, not proof that consent or device identity occurred. It cannot perform remote correction.

## Designed or not implemented

The following remain separate work:

- production Soltex publisher identity, key custody, timestamping, pin rollout, and pin rotation;
- production release-manifest/metadata keys, TLS pins, signed trust policy, and authenticated descriptor source;
- a signed installer and deterministic uninstall;
- atomic activation, rollback, crash recovery, interrupted-update recovery, and retained installer evidence;
- optional provider-name inventory without changing Windows Security Center registration;
- provider registration, minifilter, ELAM, PPL/protected service, MVI participation, cloud reputation, detection research, certification, and efficacy claims;
- the Audio, Clips, Monitoring, App Control, Privacy, Device Fabric transport/execution, NAS, Drive, and isolated Box implementation waves described elsewhere.

## Local-host state not verified by this GitHub session

The connector cannot establish the state of `C:\Users\suhai\Documents\SOL Tools`. The following remain local preflight facts, not inferred claims:

- current working directory;
- local branch and HEAD;
- local dirty/untracked state;
- whether local `WaveSlate` or related `dotnet` processes are running;
- whether the local checkout contains commits or files not present in the private remote;
- owner-host EICAR behavior;
- owner visual acceptance.

Use the exact commands in [`VALIDATION.md`](VALIDATION.md) before modifying or synchronizing the local checkout.

## Exact next implementation slice

1. Design the Device Fabric Stage 2 loopback-only signed job envelope: target binding, issuer, expiry, nonce, idempotency, policy version, replay store, cancellation, redacted receipt, and explicit approval level. Keep it non-networked and non-executing until hostile parser/state tests are green.
2. Select the production Soltex code-signing, metadata-signing, and release-manifest-signing identities; document custody/recovery; and commit only approved public subject/SPKI/key values.
3. Configure a real signed update trust policy and authenticated descriptor source, then repeat hostile transport, expiration, revocation, partial-I/O, and recovery evidence against release-candidate fixtures without installing them.
4. Specify the smallest privileged installer boundary separately from Device Fabric, including immutable input handles, exact plan binding, user confirmation, least privilege, atomic activation, rollback, repair, uninstall, reboot, and retained evidence.

Do not begin with provider registration, a driver, a service, public ingress, Defender mutations, or automatic execution of staged content.
