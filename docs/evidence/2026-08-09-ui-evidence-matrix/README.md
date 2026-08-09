# Complete native UI evidence matrix

Date: 2026-08-09

Repository: `slaveofsolace/soltex`

Merged baseline: `main` at `61639bfe7ea3a7191fcb6c70ea1f2a699aa77248` (PR #9)

Continuation branch: `sol/soltex-ui-evidence-matrix`

This packet records the verification-contract continuation after the UI-focus candidate merged. It does not change the user-facing product interface. It closes an evidence gap: the Windows workflow previously retained six default workspace captures while the native render path supports eight defaults and three progressive-disclosure states.

## Canonical matrix

`eng/capture-ui-evidence.ps1` now owns one fail-closed matrix:

| Evidence ID | Native panel argument | State | Artifact |
|---|---|---|---|
| `home-default` | `home` | default | `home-current-source.png` |
| `monitoring-default` | `monitoring` | default | `monitoring-current-source.png` |
| `monitoring-details` | `monitoring-details` | expanded | `monitoring-details-current-source.png` |
| `devices-default` | `devices` | default | `devices-current-source.png` |
| `mixer-default` | `mixer` | default | `mixer-current-source.png` |
| `mixer-more` | `mixer-more` | expanded | `mixer-more-current-source.png` |
| `clips-default` | `clips` | default | `clips-current-source.png` |
| `security-default` | `security` | default | `security-current-source.png` |
| `security-activity` | `security-activity` | expanded | `security-activity-current-source.png` |
| `remote-default` | `remote` | default | `remote-assist-current-source.png` |
| `updates-default` | `update` | default | `updates-current-source.png` |

Every capture must be a non-empty 1280x820 PNG without an error sidecar. The script records its byte length and SHA-256 digest in `artifacts/validation/render-matrix.json`.

## Provenance contract

The render manifest records two identities instead of overloading one `commit` field:

- `source_head_sha`: the branch commit supplied by the pull-request or push event;
- `tested_commit_sha`: the commit actually checked out and exercised by the runner.

The checked-out Git HEAD must equal `tested_commit_sha`. If the identities differ, the source head must be available as an ancestor of the tested commit. Both Windows workflows use checkout depth 2 so a GitHub pull-request merge checkout can prove that relationship.

`eng/record-package-identity.ps1` applies the same checks to package-smoke evidence. Its schema version 2 retains the legacy `commit` field as an alias of `tested_commit_sha` for compatibility.

## Owner-host dry run

The complete workflow-equivalent path was exercised on the owner-controlled Windows host against the unchanged native UI baseline at `61639bfe` before publication:

| Gate | Result |
|---|---:|
| Release build | Passed; 0 warnings, 0 errors |
| Identity guard | Passed |
| Design-token guard | Passed |
| Required test executables | 137/137 |
| Native matrix | 11/11 PNGs at 1280x820 |
| Stale-evidence recovery check | Rejected before capture, as designed |
| Mismatched tested-commit check | Rejected before capture, as designed |
| Self-contained `win-x64` publish | Passed; exact length and SHA-256 recorded in the generated manifest |
| Published executable render | Passed through an explicit waited process; exit code 0 |
| Package identity schema 2 | Passed; executable and render hashes recorded; Authenticode truthfully `NotSigned` |
| Package stale/identity negative paths | Rejected before evidence write, as designed |

The eleven generated PNGs and the local manifest remain under ignored `artifacts/` paths. They were inspected directly; no blank frame, missing workspace, render error sidecar, or obvious viewport clipping was observed. Expanded Monitoring, Mixer, and Security evidence intentionally moves focus to the disclosed content.

The package launch uses an explicit waited `Start-Process` result. This avoids treating an unset interactive-shell `$LASTEXITCODE` as a package failure while still failing on the process object's actual nonzero exit code.

## Hosted-viewport recovery

The first PR run correctly rejected a 1044x788 Home capture produced when the hosted Windows desktop constrained the visible WPF window to its work area. The product had been rendering `Window.ActualWidth` and `Window.ActualHeight`, so physical runner geometry leaked into supposedly comparable evidence.

The recovery keeps the 1280x820 acceptance contract. Render-smoke now configures the same complete WPF Window visual as a nonactivating, borderless, nonresizable popup fixed to the canonical viewport, which avoids normal work-area clamping without rebuilding or compositing the interface. A focused WPF regression begins with a 1044x788 request, exercises the real off-screen popup path, and proves the 1280x820 bitmap plus an unclipped bottom-right marker. Workspace drawing is clipped to its column and navigation labels bind directly to their owning buttons. Package identity also verifies and records its retained Home render's dimensions, length, and SHA-256.

## Reference-only UI observation

The user also authorized a bounded look at an already-running SteelSeries GG/Sonar window. The clean-room disposition and limits are recorded in [`RESOURCE_PROVENANCE.md`](RESOURCE_PROVENANCE.md). No screenshot, binary, asset, preset, private text, or setting was added to Soltex.

## Publication gate

Local evidence is not a substitute for GitHub evidence on the exact published head. The continuation is mergeable only after the Windows and package-smoke workflows pass on the published candidate and their manifests report the expected source/tested identities.

## Nonclaims

This packet is not owner visual acceptance, accessibility conformance, multi-scaling or multi-viewport proof, package signing evidence, antivirus efficacy evidence, installer lifecycle proof, or production-readiness approval. A deterministic capture proves only the named native render state and its recorded bytes.
