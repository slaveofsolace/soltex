# Copy-ready continuation prompt

Continue the Windows WaveSlate repository at `C:\Users\suhai\Documents\SOL Tools`.

Start by verifying the exact checkout, branch, HEAD, dirty status, running WaveSlate processes, and `docs/IMPLEMENTATION_STATUS.md`. Preserve all existing work. Do not weaken Defender, add exclusions, claim antivirus-provider status, or import proprietary SteelSeries/Malwarebytes code or data.

The Security companion's last pre-correction .NET 10 build passed 16/16 default tests, including Authenticode, WSC change registration, live WSC/Defender status, simulated outage/backoff/recovery, monitor shutdown, redacted Defender events, and benign AMSI. Static follow-up now includes eleven security hardening corrections: non-abandoned polling waits, byte-bounded process output, provider-neutral WSC precedence, System32 native resolution, pinned system-module manifests, provider/subscriber fault isolation, WSC subscriber isolation, anchored child working directory, audit-facing stderr redaction, explicit event-query error handling, and event-envelope count enforcement. Current source also adds three Remote Assist boundary tests, for 27 default and 28 expanded checks; all eleven test additions remain pending because the Codex PowerShell command path timed out during startup. The in-memory EICAR integration passed on the prior baseline. The last rendered UI evidence at `artifacts/visual/security-monitoring-v1.png` predates the Remote Assist page and Zen-inspired visual refresh.

Read `docs/REMOTE_ASSIST.md` before changing Remote Assist. RustDesk remains a separate AGPL program; do not embed its code/binary, supply passwords, configure unattended access, request elevation, install a service, or bypass visible consent. The current adapter uses explicit executable selection, byte/signature revalidation, fixed shell-free arguments, constrained peer IDs, and a local confirmation.

Next priorities, in order:

1. Establish a private GitHub remote and Windows CI without changing the verified baseline.
2. Define approved WaveSlate publisher identities and integrate Authenticode plus detached manifests into every plugin/update staging path.
3. Add monotonic signed release sequence and authenticated anti-rollback state.
4. Implement bounded archive staging with traversal, reparse, count, size, and compression-ratio defenses.
5. Add optional WSC product-name inventory without changing provider registration.
6. Add installer/update/repair/rollback/uninstall with signing and recovery evidence.
7. Extend measurements to idle CPU/memory, scan impact, audio-thread isolation, coexistence, false positives, accessibility, and representative viewports.

Keep true-antivirus work separate: minifilter, ELAM, PPL, WSC provider registration, cloud intelligence, MVI, certification, and detection-rate claims are future product/program work, not this MVP.
