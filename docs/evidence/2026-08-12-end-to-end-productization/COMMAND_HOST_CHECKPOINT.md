# Command-host recovery checkpoint

Recorded: 2026-08-13 after the benchmark product slice was staged.

## Durable repository state

- Physical implementation root: `C:\Users\suhai\Documents\soltex-product-rebuild`
- Branch: `sol/soltex-product-rebuild`
- Base HEAD: `46f18b3e6c2dacbd25df9fc7ed449718e909862a`
- Remote before this slice: `origin/sol/soltex-product-rebuild` at the same commit
- Protected owner checkout: `C:\Users\suhai\Documents\SOL Tools`, `main` at
  `bf2662de80992cfed761625642f084d3caaa0f04`, with 33 owner-owned dirty entries
  that were not modified, reset, cleaned, stashed, or discarded.
- The benchmark implementation, tests, UI, CI, documentation, canonical log,
  and reasoning ledger were staged. No commit or push for this slice had occurred.

## Completed evidence before the host anomaly

- Release solution build: zero warnings, zero errors.
- Focused benchmark suite: 5/5, including the production Quick profile in about
  2.75 seconds and scratch cleanup.
- App/control suite: 33/33.
- Current-source canonical verifier: identity and design policies passed; Release
  built cleanly; 178/178 named tests passed, including owner-host in-memory
  EICAR/AMSI detection.
- Native working-tree benchmark render: 1280x820, directly inspected with no
  clipping, hierarchy, density, or truthful-state blocker.
- Human Cortex schema 2.0.0 reasoning ledger: PASS after benchmark reconciliation.

## Host anomaly

The canonical verifier was run again after staging so the identity scanner would
include newly tracked files. That single process exceeded its 180-second command
bound. A following five-second `cmd /d /c echo SOLTEX_CANARY` also timed out.
No additional shell was probed and no ambiguous PowerShell, .NET, MSBuild, or
Soltex process was terminated. The second verifier result must not be claimed as
passing; its transcript may be partial or locked.

## Exact resume step

After the command host is known healthy, run one bounded canary. If responsive:

1. Reconcile branch, HEAD, staged/unstaged status, and exact task-owned process
   ownership. Do not touch the protected owner checkout.
2. Inspect the tail and file state of
   `canonical-verify-with-benchmark.log`; preserve the timed-out transcript under
   a distinct failure name if it is incomplete.
3. Run the staged-source canonical verifier exactly once with the pinned .NET
   10.0.302 host. Stage the final successful log.
4. Revalidate the reasoning ledger and `git diff --cached --check`, then commit
   the benchmark slice.
5. Run the exact-commit 18-state native matrix, runtime-cost probe, self-contained
   publish, installer compile/upgrade/state-preservation checks, and signing/hash
   inspection in the established safe order.
6. Reconcile exact evidence in current docs, commit the evidence update, push the
   branch, and inspect PR #11 CI.

This checkpoint is durable but not yet staged because the command bridge became
unresponsive immediately before it was written.

## Continuation audit

- First continuation after this checkpoint: the single permitted
  `cmd /d /c echo SOLTEX_CANARY` again exceeded its five-second limit.
- This is the second consecutive goal turn with the same command-host startup
  failure. No build, verifier, Git command, process query, or process termination
  was attempted afterward.
- The exact resume step above is unchanged. The active goal is not complete and
  has not yet met the three-turn threshold for a formally blocked disposition.

- Second automatic continuation after this checkpoint: the same single
  `cmd /d /c echo SOLTEX_CANARY` exceeded five seconds again.
- This is the third consecutive goal turn with the identical command-host
  startup failure. No additional command or process action followed. The active
  goal now meets the formal blocked threshold and requires a command-host
  or Windows restart before the exact resume step can run.

## Fresh resumed-goal audit

- The user explicitly resumed the previously blocked goal. The first read-only
  command in that fresh audit produced no output and was terminated safely.
- The next automatic continuation ran one `cmd /d /c echo SOLTEX_CANARY`; it also
  produced no output and was terminated. This is the second consecutive failure
  in the fresh resumed audit. No repository command, build, Git operation, or
  process cleanup followed either failure.
- The third fresh-audit turn ran the same single canary. It again produced no
  output and was terminated safely. The repeated blocker threshold is met again;
  command-host or Windows restart remains the required external state change.

## Command-host recovery

- The next explicit user resume returned `SOLTEX_CANARY` in under one second.
- Repository reconciliation confirmed implementation and remote at `46f18b3`,
  the protected owner checkout at `bf2662de` with the same 33 dirty entries, and
  no surviving verifier, build, or Soltex process from the failed bridge window.
- One resumed verifier was launched as owned PID `20896` with unique stdout and
  stderr logs and a three-minute deadline. It progressed through the named test
  executables and exited in about 36 seconds.
- Its stderr is empty. Its stdout records identity over 220 tracked text files,
  design-token policy, a zero-warning/zero-error Release build, and every suite:
  Security 32/32, supply chain 18/18, hardening 12/12, Updates 17/17, Device
  Fabric 24/24, Monitoring 16/16, Audio 21/21, Benchmarks 5/5, App/control 33/33.
- The production Quick profile completed in about 2.75 seconds and cleaned its
  scratch file. The successful stdout replaced the tracked canonical transcript
  byte-for-byte; the unique runtime log remains under ignored `artifacts`.
- Next atomic step: complete the diff-scoped security guardrail, validate/stage
  evidence, and commit the benchmark slice. The protected owner checkout remains
  out of scope.

## Defensive hardening continuation

- The command bridge recovered and the isolated worktree was re-anchored at the
  same base and remote commit. The protected owner checkout retained its 33
  owner-owned dirty entries and was not modified.
- Formal dynamic validation of two filesystem race hypotheses was intentionally
  not resumed. They remain unproven hypotheses rather than reportable findings.
- Conservative hardening now uses a one-open 32 KiB-plus-one result read, a
  per-run owned scratch directory, exclusive delete-on-close file handles, exact
  storage-length validation, identity-aware cleanup, and automatic cancellation
  when the benchmark lab stops being visible.
- The first focused build found one missing `System.Text` import and stopped. The
  single evidence-supported retry built with zero warnings/errors; Benchmarks
  passed 6/6 and App/control passed 34/34. The production Quick profile completed
  in about 2.77 seconds and left no owned scratch state.
- A fresh 1280x820 native benchmark render was directly inspected with no
  clipping, broken graphic, density, or truthful-state blocker.
- Exact resume step: run the ordinary canonical verifier, reconcile the reasoning
  ledger and staged diff, then commit and bind package/runtime evidence to the
  resulting exact commit. Do not touch the protected owner checkout.

## Exact-commit release continuation

- Benchmark and defensive hardening committed as
  `48e806092cb160e9edaef09e06552dfdbfea0530` with a clean worktree.
- Exact-commit native matrix passed 18/18; runtime lifecycle passed; the
  self-contained executable rendered and its schema-2 package identity passed.
- Unsigned `Soltex.exe` and the unsigned 1.0.2 per-user installer compiled with
  exact hashes recorded in `EXACT_COMMIT_RELEASE.md`.
- Installer execution was denied before launch. Reconciliation proved the
  existing installation remained version 1.0.0 at its prior hash with no related
  process. Upgrade/state preservation are not claimed.
- Exact resume step: commit this evidence-only reconciliation, push the branch,
  inspect required hosted Windows/package workflows, and retain any provider-
  specific optional failure without converting it into an efficacy claim.
