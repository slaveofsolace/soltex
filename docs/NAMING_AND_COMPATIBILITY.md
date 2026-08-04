# Soltex naming and compatibility contract

This document is the migration contract for replacing the WaveSlate working name with the Soltex product identity. It separates identifiers that may change immediately from identifiers that must remain compatible with installed state or historical evidence.

## Baseline

- Migration branch: `refactor/soltex-identity-and-repo-coherence`
- Stacked base: `feat/soltex-update-planner-v1`
- Base commit: `40c7e73452dc6c11fcd1f5711ec6360210595a44`
- Windows verification: run `30918555568`, job `92022851868`
- Evidence artifact: `soltex-windows-evidence-30918555568-1`
- Artifact digest: `sha256:01a8d3923249fad5cf9c48605fa3cf95c119579da4a89686a977ac74089f8384`
- Baseline result: Release build 0 warnings and 0 errors; required suites and native renders passed. The opt-in EICAR matrix recorded 27/28 checks because the available AMSI provider returned result 1; this is evidence of the provider response, not a claim of independent antivirus protection.

The pre-migration inventory contains 351 WaveSlate-form references across 95 files. They are classified below so a broad text replacement cannot accidentally orphan state, weaken a trust boundary, or rewrite history.

## Canonical identity

| Concern | Canonical value | Migration rule |
| --- | --- | --- |
| Product display name | `Soltex` | Change immediately in current UI and current documentation. |
| Solution | `Soltex.sln` | Rename with project paths updated atomically. |
| Application project and namespace | `Soltex.App` | Rename project, assembly, namespaces, XAML class names, and test references together. |
| Security project and namespace | `Soltex.Security` | Rename project, assembly, namespaces, friend assemblies, and tests together. |
| Remote-assist project and namespace | `Soltex.RemoteAssist` | Rename project, assembly, namespaces, and tests together. |
| Update project and namespace | `Soltex.Update` | Rename project, assembly, namespaces, friend assemblies, and tests together. |
| Device-fabric project and namespace | `Soltex.DeviceFabric` | Rename project, assembly, namespaces, and tests together. |
| Signed release product | `Soltex` | Already canonical in schema 2. Remains strict and case-sensitive. |
| Fresh local data root | `%LocalAppData%\\Soltex` | Use only when no legacy root exists. Never merge roots. |
| Fresh import inbox | `%LocalAppData%\\Soltex\\Imports` | Resolve under the selected product root. |

Central product metadata is the source of truth for current display, assembly, manifest, path, and environment-variable identifiers. Tests enforce that current source does not introduce ad-hoc legacy names.

## Persistent-state compatibility

Existing authenticated state is security-sensitive. Renaming a folder or a DPAPI descriptor without a compatibility plan can make valid state unreadable, invite ambiguous precedence, or conceal a partial migration.

The resolver follows these fail-closed rules:

1. Normalize the supplied local-app-data root and reject any reparse point in the existing path.
2. Inspect the canonical `Soltex` root and legacy `WaveSlate` root without creating either one.
3. If neither root exists, select and create the canonical `Soltex` root.
4. If exactly one root exists, select it. A legacy selection is an explicit compatibility lookup, not a silent migration.
5. If both roots exist, reject startup with a bounded conflict error. Never merge, copy, delete, or choose one by timestamp.
6. Reject a selected product root or relevant child path when it is a reparse point.
7. Keep all authenticated state, quarantine metadata, audit chain, release-sequence state, update-planning journals, and imports under the one selected root.

This first compatibility release deliberately performs no automatic file move. A future one-time migration may be added only with an authenticated migration journal, crash recovery, conflict tests, and a separately reviewed rollback story.

### Persistent identifiers

| Existing identifier | Status | Reason / removal condition |
| --- | --- | --- |
| `%LocalAppData%\\WaveSlate` | Temporary compatibility alias | Selected only when it is the sole existing product root. Remove only after a separately validated migration has shipped and the supported retention window has elapsed. |
| `%LocalAppData%\\WaveSlate\\Security` | Temporary compatibility path | Resolved beneath the selected legacy root; never merged with canonical state. |
| `%LocalAppData%\\WaveSlate\\Imports` | Temporary compatibility path | Resolved beneath the selected legacy root; never merged with canonical imports. |
| DPAPI description `WaveSlate authenticated state` | Cryptographic compatibility invariant | Remains unchanged so existing per-user protected keys stay decryptable. It is not user-facing identity. Change only through an explicitly versioned key migration with recovery tests. |
| Integrity-manifest schema 1 product `WaveSlate` | Versioned legacy protocol value | Accepted only for schema 1 signed import manifests. New schema-1 output uses `Soltex`; remove legacy acceptance only when those signed artifacts are outside the supported compatibility window. |
| Signed-release schema 2 product `Soltex` | Canonical protocol value | No legacy alias is added. Anti-rollback and signature checks remain strict. |
| `WAVESLATE_RUN_EICAR` | Temporary test alias | CI and scripts move to `SOLTEX_RUN_EICAR`; the test runner accepts the legacy alias during the stacked-PR transition. Remove after all supported automation uses the canonical name. |
| `WAVESLATE_TEST_CHILD_MODE` | Test-only alias | Move to `SOLTEX_TEST_CHILD_MODE`; temporary dual-read only if a child process can span versions. |
| Defender child-process variables `WAVESLATE_SCAN_PATH`, `WAVESLATE_EVENT_START_UTC`, `WAVESLATE_EVENT_MAX` | Ephemeral implementation detail | Rename atomically with the embedded scripts. They are process-local and not persisted. |
| Mutexes, named locks, event sources, scheduled-task names | Inventory required before change | No such product-named operating-system object was found at the baseline. Identity enforcement fails if one is introduced without an explicit entry here. |

## Reference classification

### Change in the current migration

- Current product copy in application XAML, window titles, commands, errors, audit descriptions, README, security guidance, architecture documents, and live handoff material.
- Solution, project directories, project files, assembly names, root namespaces, C# namespaces/usings, XAML class names, project references, friend assemblies, test project names, workflow paths, and verification script paths.
- Current test fixtures and assertions, except when they intentionally exercise a legacy compatibility input.
- Process-local environment variables and CI configuration.
- Current manifest generators and fixtures so newly produced schema-1 manifests identify `Soltex`.

### Retain with an explicit allowlist

- The DPAPI description listed above.
- Legacy path and schema values inside the compatibility resolver and its focused tests.
- Historical evidence packets, immutable hashes, old commit messages, and quoted prior-run output. Historical material is not rewritten to make the past look canonical.
- Third-party names, license notices, and upstream attributions. Nothing in this migration fabricates authorship or removes provenance.

### Historical documents

Evidence directories remain append-only. Redundant planning prompts may be archived or replaced by a short pointer, but factual reports keep the product name and commit identity that were true when generated. The identity checker therefore scans current implementation and canonical documentation while using a path-and-token allowlist for compatibility code and historical evidence.

## Failure and recovery behavior

- Ambiguous dual roots: fail before creating stores or monitors; report both bounded paths and provide no automatic repair.
- Reparse point in a trust path: fail before reads or writes.
- Missing root: create only the canonical root and required children.
- Existing legacy root: continue in place, label the compatibility selection in diagnostics, and do not mutate canonical storage.
- Torn or tampered authenticated files: existing authenticated-store validation remains authoritative and fail-closed.
- Cancellation or shutdown: no migration task is left running because this phase performs lookup rather than background copying.

## Removal gates

A compatibility alias can be removed only when all of the following are true:

1. no supported installed release can still create it;
2. migration telemetry or a local diagnostic (without uploading private paths) shows the alias is no longer in use;
3. downgrade and crash-recovery behavior is documented;
4. hostile tests cover conflicts, tampering, reparse points, and interrupted migration;
5. a release note identifies the compatibility-window change.

Until those gates are met, compatibility identifiers are deliberate security boundaries, not unfinished branding work.
