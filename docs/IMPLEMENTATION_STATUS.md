# Implementation status

Snapshot: 2026-08-03  
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

Implementation commit `6ea85727935564122c5237ae9c3b85cd81cbbc72` was exercised in pull-request merge preview `811b55ad875c79d3b2c50f746ae853c291ad210e` by GitHub Actions run `30858289994` on Windows Server 2025 (`10.0.26100`, image `windows-2025-vs2026` `20260728.188.1`) with .NET SDK `10.0.302`.

| Gate | Result | Scope |
|---|---:|---|
| Release build | 0 warnings, 0 errors | Entire `WaveSlate.sln`, including the new test project |
| Existing focused suite | 27/27 passed | Security companion, monitoring boundaries, Remote Assist regressions |
| Supply-chain suite | 18/18 passed | Publisher policy, signed release, sequence state including cross-process lock, bounded ZIP staging |
| Opt-in EICAR interoperability | 27/28 | Hosted AMSI provider returned native result `1`; owner-host evidence remains pending |
| Security native render | Passed | 1044×788 render-smoke output created |
| Remote Assist native render | Passed | 1044×788 render-smoke output created |
| Render artifact check | Passed | Both PNGs present; no `*.error.txt` output |

Retained workflow evidence:

- run: `30858289994`;
- job: `91834318247`;
- artifact: `soltex-windows-evidence-30858289994-1`;
- artifact ID: `8873339056`;
- downloaded artifact ZIP size: 224,240 bytes;
- artifact ZIP SHA-256: `B2720273046CA62D9D5D675AEA13E48CE6D5CAE35ACAB95F5B3725A13B68538C`;
- retention configured by the workflow: 30 days.

The hosted Windows Server image did not expose a usable live `wscapi.dll` boundary. The provider-neutral health test therefore proved bounded failure/fallback behavior and an `Unknown` state, not successful provider inventory on that host. Native rendering proves that both current panels initialize and capture; owner visual acceptance remains pending. Pixel inspection identified unresolved text truncation in the Security scan subtitle and event-detail column.

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

**Nonclaims:** staged content remains inert. It is not executed, loaded as a plugin, recursively unpacked, copied into an installation directory, or treated as approved merely because extraction succeeded. No orchestrator yet binds archive acquisition, staging, signed-manifest verification, Authenticode publisher authorization, sequence acceptance, transactional installation, or recovery.

## Implemented: Remote Assist boundary

`WaveSlate.RemoteAssist` remains a narrow adapter for a separately installed RustDesk executable. It implements explicit selection, reparse rejection, SHA-256 and Authenticode revalidation, constrained peer IDs, a fixed shell-free `--connect` plan, local confirmation, and peer-ID-free audit events.

It does not embed or link RustDesk AGPL code, store remote passwords, enable unattended access, install services, request elevation, open listeners, hide sessions, or bypass local consent.

## Designed or not implemented

The following remain separate work:

- production Soltex publisher identity, key custody, timestamping, pin rollout, and pin rotation;
- a signed installer and deterministic uninstall;
- authenticated update transport and bounded download staging;
- an update transaction that composes all current primitives;
- atomic activation, rollback, crash recovery, interrupted-update recovery, and retained installer evidence;
- optional provider-name inventory without changing Windows Security Center registration;
- provider registration, minifilter, ELAM, PPL/protected service, MVI participation, cloud reputation, detection research, certification, and efficacy claims;
- the Audio, Clips, Monitoring, App Control, Privacy, Device Fabric, NAS, Drive, and isolated Box implementation waves described elsewhere.

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

1. Select the production Soltex code-signing and release-manifest signing identities, document custody and recovery, and record approved subject/SPKI/public-key values without committing private material.
2. Define signed pin-rotation and release-key-rotation rules with explicit overlap and rollback behavior.
3. Build a non-elevated update planner that composes acquisition, bounded staging, manifest verification, publisher authorization, sequence decision, and a user-visible plan without installing anything.
4. Only after that planner passes hostile-fixture and recovery tests, design the signed installer/elevation boundary, atomic activation, rollback, repair, uninstall, and retained release evidence.

Do not begin with provider registration, a driver, a service, public ingress, Defender mutations, or automatic execution of staged content.
