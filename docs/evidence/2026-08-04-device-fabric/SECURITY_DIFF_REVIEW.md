# Device Fabric Stage 1 security diff review

Date: 2026-08-04
Implementation commit: `8ca28f8aa1f50de929787fe1c1cbd23b96b3f6e9`
Comparison start: `849ce088e0b7d9fa4a744688a10081dd7183bd3c`

## Scope

The review covers `src/WaveSlate.DeviceFabric`, its focused test project, solution registration, and the Windows workflow gate. Documentation-only reconciliation after the implementation commit is also included. The original dirty `C:\Users\suhai\Documents\SOL Tools` checkout is outside the edit boundary.

## Security properties inspected

- Catalog lookup is exact and ordinal; no caller-defined capability becomes executable policy.
- Every known capability decision is bound to the supplied target manifest and denied when the target did not advertise it.
- Read-only observation requires device-local policy; state-changing requests require visible per-job local consent.
- RustDesk handoff preparation additionally requires the external client to be visible.
- Device IDs, display names, and agent versions are length/character bounded; control-character injection is rejected.
- Capability and inventory inputs are defensively copied, bounded, and duplicate-checked.
- Defender mutation requests are rejected for non-Windows manifests.
- The new production project contains no process launch, shell, network, native import, credential, registry, filesystem mutation, installer, elevation, service, driver, or protection-weakening API.

## Resolved during review

The first policy draft checked exact catalog membership but did not require the target device to advertise that capability. Commit `a29619aae4723b1b6c235bdb8d29dbacb44ff37c` closes that capability-confusion path and adds focused missing-target and unadvertised-capability regressions.

## Current findings

No actionable code vulnerability was found within the non-executing Stage 1 boundary after the target-binding correction.

`DF-SEC-GATE-001` remains a hard gate for every later executor: a structurally valid manifest is not an authenticated device identity, and the policy booleans are not authenticated proof of local policy, consent, or client visibility. Stage 2 must keep execution absent while it defines signed target/issuer binding, enrollment, expiry, nonce, replay persistence, policy versioning, cancellation, and redacted receipts. A capability executor must not consume this model as an authorization proof until those boundaries and hostile tests exist.

## Verification

- Local compatibility compile: clean with warnings treated as errors.
- Local focused suite: 20/20 passed.
- GitHub Actions run `30918120029`, job `92021363535`: .NET SDK `10.0.302`, Release build 0 warnings/0 errors, Device Fabric 20/20 in 37.5 ms.
- Merge preview: `035de520a6ea346b9aeb08270fa4f72af86d59c0`.
- Retained artifact ID `8895955309`, digest `sha256:02c121da84a68988b0d50b1f8cb3cc50c72d299ac3a1ca3d4c7d1c4146aca31a`.
- Human Cortex ledger validation: schema 2.0.0, no issues.

## Nonclaims

This review does not prove device identity, consent authenticity, remote-access security, network security, cross-platform behavior, production readiness, or full/unsupervised remote correction. It does not authorize a listener or executor.
