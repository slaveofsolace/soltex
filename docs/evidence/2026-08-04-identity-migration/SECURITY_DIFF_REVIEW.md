# Soltex identity migration security diff review

Date: 2026-08-04
Comparison start: `40c7e73452dc6c11fcd1f5711ec6360210595a44`
Verified implementation commit: `42f10865fcbcd31f7b26dbb98446d09cfc69285d`

## Scope

This review covers central product identity, the solution/project/assembly/namespace migration, default local-data-root selection, schema-1 manifest compatibility, process-local environment names, identity enforcement, current documentation, and the WPF compatibility diagnostic. Append-only evidence and archived prompts remain historical. The protected dirty `C:\Users\suhai\Documents\SOL Tools` checkout was not edited.

## Security properties inspected

- Fresh profiles create only the canonical product root.
- A sole existing compatible root is selected in place; the code does not copy, merge, delete, or choose state by timestamp.
- Dual roots and non-directory name collisions fail before authenticated stores initialize.
- Existing path components and selected product/security/import/state/quarantine directories reject reparse points.
- Explicit test roots retain their prior semantics and are labeled `Explicit` rather than mistaken for migrated storage.
- The DPAPI description remains byte-for-byte stable through one centralized compatibility constant, preserving existing protected keys.
- Schema-1 integrity manifests are signature-checked before accepting only the current or one versioned legacy product identifier. Schema-2 signed releases remain Soltex-only.
- Canonical CI and process-local environment variables use the new identity; the old EICAR variable has a temporary, exact dual-read compatibility alias.
- Current source and documentation are scanned by an identity gate. Every permitted legacy token has a path/line or historical-prefix reason.
- The WPF activity surface reports compatible-root use without exposing a local path and explicitly says no files moved.

## Failure and recovery review

- Ambiguous roots: fail closed and require deliberate owner resolution; no automatic destructive repair is offered.
- Reparse or file collision: fail before state creation/read.
- Tampered authenticated state: existing HMAC/DPAPI and recovery checks remain authoritative.
- Concurrent old/new first launch can theoretically leave two roots if each creates a different name between checks. The subsequent startup fails closed. Automatic cleanup is intentionally absent because safe ownership cannot be inferred after that race.
- A future physical migration requires an authenticated migration journal, exclusive version-aware coordination, crash recovery, downgrade rules, and hostile tests. This diff does not claim that migration exists.

## Resolved during validation

Two intermediate workflow runs found compile-only mistakes in newly added test lambdas. The production projects compiled; the lambdas were corrected without changing the storage policy. Final run `30921441649` built the exact merge preview with zero warnings/errors and passed every required suite and native render.

## Current findings

No actionable code vulnerability was found within this compatibility-lookup boundary after the focused fixes. The main residual risk is the documented cross-version first-launch race; its failure mode is an explicit dual-root stop rather than silent state precedence or data loss.

## Evidence

- Windows run `30921441649`, job `92032791166`
- Merge preview `54c160942a0f2b0837afaa87ccdd4f7b9aa301d8`
- Artifact `8897293182`
- Artifact digest `sha256:ea7781b0296147362d4546abe5076ec0282f0f15f30256ebb3f5d4961f5f6195`
- Identity policy passed across 106 tracked text files with 9 reasoned allowlist entries
- Default security suite 31/31; supply-chain 18/18; hardening 12/12; update 17/17; Device Fabric 20/20

## Nonclaims

This review does not establish production antivirus efficacy, safe automatic state migration, resilience against a fully compromised same-user account, owner visual acceptance, installer readiness, remote-control security, or unattended correction. It authorizes no listener, executor, elevation, service, driver, exclusion, or protection weakening.
