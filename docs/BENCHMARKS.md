# Local benchmark lab

Soltex Performance includes one short, cancelable **Quick local benchmark**. It
measures named workloads on the current machine and deliberately does not create
a synthetic PC score or a cross-machine ranking.

## What it measures

| Stage | Workload | Quick profile |
|---|---|---:|
| Processor | SHA-256 throughput over reusable 1 MiB blocks | 1.5 seconds |
| Memory | managed copy throughput between reusable 8 MiB buffers | 1.2 seconds |
| Temporary storage | sequential write and read of one scratch file | 32 MiB |

Each result includes its profile identifier/version, start and completion time,
logical processor count, optional pre-run CPU observation, stage duration, and
MiB/s measurement. The fixed result copy states that power plans, thermal state,
storage cache, background work, and hardware differences affect the numbers.

## Runtime and cleanup contract

- Entering the benchmark lab suspends live Performance sampling so Soltex does
  not compete with the measured workload.
- Leaving Performance, switching back to the live overview, minimizing, hiding,
  or closing Soltex cancels an active run before live sampling can resume.
- The Run command captures a short pre-run CPU observation, then executes the
  three stages sequentially.
- Cancel is available during the run. Each run creates a private, randomly named
  child directory with an open owner marker and one `DeleteOnClose` payload. The
  write and bounded read use the same exclusive file handle, require the exact
  expected byte length, and remove only the owned payload and now-empty run
  directory on success, cancellation, or failure.
- Stage durations are limited to 25 milliseconds through 5 seconds. Storage is
  limited to aligned sizes from 1 MiB through 64 MiB; the shipped Quick profile
  uses 32 MiB.
- The scratch root is resolved to a full local path. A non-directory or reparse
  root is rejected before measurement.
- No elevation, Windows setting, service, driver, security exclusion, GPU load,
  network request, or third-party process is used.

## Saved result and privacy

Only the latest Quick result is saved. The UTF-8 JSON document is schema-bound,
limited to 32 KiB, written through a unique temporary file, and atomically moved
into place. Loading opens the file once and reads at most 32 KiB plus one overflow
byte before decoding or deserializing it. It contains measurements and bounded
run context only. It does not contain a hostname, account name, storage path,
device serial, hardware ID, process list, or raw telemetry history.

Malformed, oversized, future-dated, unsupported-profile, non-finite, and
out-of-range documents recover visibly to **no saved result**. Clear saved result
requires confirmation and deletes only this document.

## Evidence and nonclaims

The benchmark test executable runs the production Quick profile end to end,
requires positive finite measurements, enforces a 15-second wall-clock ceiling,
and verifies owned scratch cleanup. Separate benign tests cover cancellation,
unexpected scratch length, profile/input bounds, one-open bounded persistence,
truncation/change/oversize recovery, UI disclosure, and telemetry suspension.

Defensive hardening was applied for two filesystem race hypotheses. Real-world
exploitability was not dynamically established because policy-safe validation
was intentionally not attempted; no reparse-point, cross-user, or sensitive-path
demonstration is part of the evidence.

This is not a hardware certification, thermal stress test, stability test,
diagnostic verdict, GPU benchmark, or claim that results are comparable across
machines. Soltex does not recommend overclocking or hardware changes from these
measurements.
