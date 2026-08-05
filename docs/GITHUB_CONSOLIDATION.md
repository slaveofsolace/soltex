# GitHub consolidation contract

Snapshot: 2026-08-04

Repository: `slaveofsolace/soltex`

Selected integration branch: `release/soltex-v1-foundation`
Selected pull-request title: **feat: establish the Soltex V1 foundation**

## Objective

Present the complete verified Soltex foundation as one coherent review unit without rewriting tested history, falsely claiming human authorship, or deleting recovery branches before the integrated result is proven.

Branch organization cannot prove whether code was human- or AI-authored. The repository therefore uses factual product language, normal engineering branch names, exact evidence, and proportional claims. It does not add AI-generated labels, fabricated authorship statements, or cosmetic history rewrites.

## Audited remote state

The GitHub app was checked on 2026-08-04. Six remote branches were visible and the active product stack was exactly linear.

| Branch | Audited tip | PR | Relationship |
|---|---|---:|---|
| `main` | `eb0d2ffeacc52d16b46795cd4facdd17a5816b32` | — | default integration base |
| `chore/current-source-gate-2026-08-03` | `d1f91209` | #1 | already merged; obsolete recovery branch |
| `feat/security-supply-chain-hardening` | `39eaf628f6add6c89963a81b7c1971c2f74f02a1` | #2 | 16 commits after `main` |
| `feat/soltex-update-planner-v1` | `40c7e73452dc6c11fcd1f5711ec6360210595a44` | #3 | 17 commits after security |
| `refactor/soltex-identity-and-repo-coherence` | `57607a35da14968c0d729795a857fd250b6566d9` | #4 | 7 commits after updater |
| `feat/soltex-immersive-workspace-v1` | `bf57b7ff3007fc14bd8828ba0a3e1861535ef1dd` at audit | #5 | 14 commits after identity |

At the audited remote tip, comparison against `main` reported `ahead_by=54`, `behind_by=0`, merge base `eb0d2ffeacc52d16b46795cd4facdd17a5816b32`, and 137 changed files. PRs #2 through #5 were open and mergeable. Branch-protection/ruleset state remained **UNKNOWN** because the connected API did not expose it and the local GitHub CLI was not authenticated.

PR #5's stale planned-work description was replaced through the GitHub app with the implemented scope and exact evidence at remote head `bf57b7ff3007fc14bd8828ba0a3e1861535ef1dd`. Its branch, stacked base, draft state, and commits were not changed.

## Selected strategy

1. Finish and publish the final documentation/reference-system commit on `feat/soltex-immersive-workspace-v1`.
2. Require the final source head to pass the complete Windows workflow.
3. Create `release/soltex-v1-foundation` at that exact verified source SHA; do not synthesize or replay commits.
4. Open one draft PR from that branch to `main` with the combined product/evidence description below.
5. Require a green workflow for the exact integration head and its GitHub merge preview.
6. Review the aggregate diff, protected-branch/ruleset state, and merge method before any merge.
7. If approved, use a merge commit. Do not squash, rebase, or force-push because the retained evidence names exact ancestor commits.
8. After the merge and post-merge Windows workflow pass, prove every retained tip is reachable from `main`.
9. Only then close #2 through #5 as superseded and delete obsolete remote branches.

Until step 8 succeeds, every stacked branch remains a recovery reference and must not be deleted.

## Consolidated PR description

### Outcome

This pull request consolidates the complete Soltex V1 foundation into one reviewable integration branch targeting `main`. It supersedes stacked delivery PRs #2, #3, #4, and #5 without rewriting their tested history.

### Included foundation

- supported Windows security-health, AMSI, Defender-event, Authenticode, hashing, quarantine, import-monitoring, and supply-chain boundaries;
- fail-closed signed update planning and recovery inspection without installation or execution;
- canonical Soltex solution/projects/namespaces plus authenticated legacy-state compatibility;
- exact non-executing Device Fabric Stage 1 policy and sanitized unenrolled local observation;
- visible, consent-first external RustDesk handoff with no embedded AGPL runtime;
- shared WPF design resources and real Home, Monitoring, Devices, Security, Remote Assist, and Updates workspaces;
- bounded CPU, memory, process, and fixed-volume observation with finite histories and explicit failure/recovery states;
- factual clean-room reference-system documentation covering AppControl, Zen, CAM, Sonar, RustDesk, Tailscale, Defender/Malwarebytes, Drive, Box, and Windows audio.

### Verification fields required before merge

- exact final source/integration SHA;
- comparison to `main`, including ahead/behind, merge base, and changed-file count;
- proof that security, update, and identity tips remain ancestors;
- exact head workflow run/job/conclusion;
- exact merge-preview SHA and workflow run/job/conclusion;
- artifact name, ID, byte size, SHA-256 digest, and expiry;
- base/head branches and SHAs, draft/mergeable state, reviews, comments, and unresolved threads;
- verified merge-method availability and branch-protection/ruleset status;
- no-running-runtime and protected-owner-checkout preservation checkpoint.

### Nonclaims

The consolidated foundation is not production antivirus efficacy, a Windows security provider, production update activation, authenticated device enrollment, unsupervised remote correction, unattended RustDesk control, GPU/network telemetry, benchmark comparability, accessibility conformance, multi-scaling acceptance, or owner visual approval.

## Cleanup proof

After an approved merge and green post-merge workflow:

1. verify `main` contains the final integration SHA plus `39eaf628`, `40c7e734`, and `57607a35`;
2. close #2, #3, #4, and #5 as superseded with a link to the consolidated PR;
3. delete the merged `chore/`, `feat/`, `refactor/`, and temporary `release/` branches;
4. re-enumerate remote branches and confirm only intended long-lived branches remain;
5. retain CI artifacts and documentation evidence even after branch deletion.

No close/delete operation is authorized by a green head workflow alone. The consolidated merge, ancestry proof, and post-merge workflow must all succeed first.
