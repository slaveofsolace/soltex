# Personal device fabric

## Product direction

Soltex can become a private control surface for the user's Windows PCs, Macs, NAS, Google Drive, and a separately governed work Box account. The central design rule is that remote desktop is **remote hands**, not the command architecture. RustDesk remains a visible, consent-based fallback when a person needs to see or control a screen. Small Soltex agents execute narrowly typed jobs on each device.

Stage 1 of this direction is implemented as a non-executing policy foundation. The device agent, signed job protocol, Tailscale integration, NAS, Google Drive, Box, and AI orchestration remain proposals and are not implemented in the current repository.

## Implemented Stage 1 boundary

`src/Soltex.DeviceFabric` provides immutable device manifests and inventory snapshots plus an exact six-capability catalog:

- `system.health.observe`;
- `security.protection.observe`;
- `update.journal.inspect`;
- `security.defender.quick_scan.request`;
- `security.defender.intelligence_update.request`;
- `remote.rustdesk.handoff.prepare`.

Every modeled decision is bound to a structurally valid target manifest. Read-only observations require device-local policy; both Defender requests and RustDesk handoff preparation require visible, per-job consent, and a RustDesk handoff is denied unless the external client remains visible. The model copies and bounds caller-supplied collections, rejects duplicate/unknown capabilities and devices, rejects injectable identifiers and display names, and limits Defender request declarations to Windows.

The focused suite passed 20/20 on the .NET 10 Windows gate in run `30918120029` (37.5 ms diagnostic wall-clock time). It expressly rejects generic shell, arbitrary download-and-execute, and hidden-control identifiers. This is model evidence only: manifest identity and consent booleans are not authenticated by this stage, and there is no collector, network transport, listener, signed envelope, device enrollment, replay store, executor, receipt pipeline, or Device Fabric UI.

## Proposed runtime shape

```text
voice/text request
      |
      v
Soltex controller / intent planner
      |
      +-- policy evaluation + human preview
      +-- short-lived signed job envelope
      |
      v
private device network (separately installed Tailscale client)
      |
      +-- Windows agent  ---- typed local capabilities
      +-- macOS agent    ---- typed local capabilities
      +-- NAS connector  ---- files, indexes, evidence mirror
      +-- Drive connector ---- personal cloud profile
      +-- Box connector   ---- isolated work profile
      |
      +-- RustDesk handoff ---- visible remote hands / recovery
```

The controller may translate a user request such as "ask the Windows PC to export the report" into a typed job, but it must not expose a generic remote shell. A job names a registered capability, constrained arguments, target device, issuer, expiry, nonce, idempotency key, policy version, and required approval level. The target agent independently validates that envelope before doing anything.

## Responsibilities and boundaries

### Soltex device agent

- Runs unelevated by default and advertises an explicit capability manifest.
- Supports typed operations such as open an approved app, query system status, copy an approved file, or prepare a RustDesk handoff.
- Has no arbitrary command, script, PowerShell, AppleScript, or terminal endpoint.
- Keeps device-local allow rules and high-impact approval decisions. The NAS is never the source of authorization.
- Stores connector secrets only in Windows Credential Manager/DPAPI or macOS Keychain.
- Rejects expired, replayed, mis-targeted, incorrectly signed, or policy-incompatible jobs.
- Returns a bounded receipt containing outcome, duration, redacted diagnostics, and an integrity reference; it does not return arbitrary file contents unless the capability explicitly permits that transfer.
- Requires visible device-local consent for screen control, credential access, security changes, software installation, destructive file actions, and privilege elevation.

### Private mesh

Tailscale is proposed as a separately installed private-network dependency, not as code embedded in Soltex. A tailnet can provide reachability and device identity, while Soltex still applies its own job authorization and replay protection. Network membership alone is not permission to execute a capability.

Soltex V1 must not automatically weaken Tailscale ACLs, expose public ingress, open router ports, publish the NAS, or treat a reachable IP address as a trusted user. Initial integration should consume only locally available connection/device state and user-supplied approved targets.

### RustDesk remote hands

The existing `Soltex.RemoteAssist` external-process boundary remains intact. RustDesk owns capture, transport, authentication, input, session consent, elevation, updates, and termination. The device fabric may prepare or deep-link a user-approved handoff, but it must not supply passwords, enable unattended access, hide the RustDesk UI, install its service, or treat a remote session as proof that an AI job is authorized.

### NAS

The NAS may hold shared files, a searchable index, backups, exported reports, and an append-only mirror of redacted execution receipts. It must not hold live OAuth refresh tokens, device private keys, approval policy, or the authoritative command queue. A compromised or rolled-back NAS must not grant a new capability or replay an old job.

### Google Drive personal profile

Read-write access is a valid product target. The first connector should support search, upload, download, create folder, move, rename, trash, restore, and a user-configurable Soltex inbox. It should use the user's OAuth grant, minimize requested scopes to implemented operations, surface the active account and destination before mutation, and provide deterministic conflict behavior.

Destructive or broad operations require a preview and explicit confirmation. Synchronization rules must be directional and folder-bounded; "sync everything" is not a safe default. OAuth tokens remain in the operating-system secret store and are never copied to the NAS, logs, prompts, or another device.

### Box work profile

Box is a distinct work trust domain, even if the same person controls the Soltex UI. It needs a separate connector process/profile, separate credentials, separate search index, separate audit stream, and clear work-account chrome. Enterprise policy and administrator restrictions remain authoritative.

No personal Drive-to-work Box, work Box-to-personal Drive, work Box-to-NAS, or work Box-to-AI-content transfer occurs by default. Each cross-domain transfer needs an explicit policy, visible source/destination preview, per-job approval, and auditable reason. Soltex must not claim that local user consent overrides employer data-handling rules.

## Command flow

1. Capture voice or text and show the interpreted intent.
2. Resolve one target device and one registered capability.
3. Show a concrete preview: action, target, files, cloud account, side effects, and approval requirement.
4. Issue a short-lived, nonce-bearing job envelope.
5. Re-evaluate policy on the target device.
6. Ask for device-local consent when the capability demands it.
7. Execute with bounded time, memory, output, and cancellation.
8. Return a redacted receipt and surface recovery guidance on failure.
9. Mirror only the allowed receipt subset to the NAS.

AI planning is advisory. It may propose a job, but it cannot mint broader rights than the user's configured controller identity and the target device's local policy already allow.

## Failure and recovery model

| Condition | Required behavior |
|---|---|
| Target offline | Queue nothing indefinitely; show offline and let the user retry |
| Mesh unavailable | Fail closed; do not fall back to public exposure |
| Envelope expired or replayed | Reject and audit without execution |
| Capability/version mismatch | Reject with the supported capability version |
| Local consent denied or times out | Perform no action and return a denial receipt |
| Partial file transfer | Keep a temporary file, verify length/hash, then atomically publish or remove it |
| Cloud token revoked | Stop the connector, preserve unsent work locally, and request reauthorization |
| Sync conflict | Preserve both versions or require a choice; never silently overwrite by timestamp alone |
| NAS unavailable/rolled back | Continue local authorization; pause evidence mirroring |
| RustDesk launch/session failure | Leave recovery in the visible RustDesk client; do not auto-retry credentials |

## Delivery sequence

0. **Completed:** evidence the current Security V1 and Remote Assist verification gates.
1. **Completed at the model boundary:** add immutable device inventory and exact capability policy with focused non-networked tests.
2. Implement the Windows agent/controller protocol over loopback, with signed envelopes, replay defense, approvals, cancellation, and receipts.
3. Add a macOS agent with the same protocol and platform-specific secret storage.
4. Add optional external Tailscale reachability without modifying ACLs or public ingress.
5. Connect the existing consent-first RustDesk handoff to registered device records.
6. Add a bounded NAS file/index/evidence connector that has no authorization role.
7. Add the personal Google Drive read-write connector and deterministic sync rules.
8. Add the isolated Box work profile only after its cross-domain policy and enterprise review are explicit.
9. Add voice/AI intent translation last, after every executable capability is deterministic without AI.

## Acceptance evidence for each implemented phase

- Threat model and exact capability inventory.
- Unit and integration tests for allow, deny, expiry, replay, cancellation, timeout, partial failure, recovery, and redaction.
- Cross-device runtime evidence on the actual supported Windows/macOS versions.
- Idle CPU, working set, network heartbeat bytes, command latency, and file-transfer measurements.
- Proof that no listener is publicly reachable and no connector secret appears in logs or the NAS mirror.
- Native UI captures at representative sizes plus explicit human visual review.
- Accurate nonclaims for capabilities, providers, accounts, and platforms not tested.

## Explicit nonclaims

This proposal is not a zero-trust certification, MDM product, endpoint-detection platform, secure remote-access guarantee, backup guarantee, data-loss-prevention system, or proof of compliance with an employer's policies. Tailscale, RustDesk, Google Drive, Box, the NAS, Windows, and macOS remain separate trust domains with their own security and availability behavior.
