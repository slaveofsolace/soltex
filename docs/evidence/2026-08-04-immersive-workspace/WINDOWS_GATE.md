# Soltex immersive-workspace Windows gate

Date: 2026-08-04

Evidence class: frozen implementation checkpoint

Repository: `slaveofsolace/soltex`

Branch: `feat/soltex-immersive-workspace-v1`

Implementation commit: `2a0699b2ca77b30fa636279b1d5ecab603a8bde9`

## Workflow identity

- Run: `30925606488`
- Job: `92046999983`
- Artifact: `soltex-windows-evidence-30925606488-1`
- Artifact ID: `8899014386`
- ZIP bytes: 626,093
- GitHub-recorded SHA-256: `71905016B3A67F8CE340D90C2E404DEBFB185C3DAE8A973F21B80F1B8A94515`
- Independently downloaded-byte SHA-256: `71905016B3A67F8CE340D90C2E404DEBFB185C3DAE8A973F21B80F1B8A94515`
- Local inspected extraction: `C:\Users\suhai\.codex\visualizations\2026\08\02\019fc3bd-0b97-7560-925e-28f62969e8d3\soltex-wave-b-run-30925606488\extracted`

The matching independent digest binds this review to the downloaded artifact bytes. It does not authenticate GitHub itself beyond the repository connection and the recorded workflow metadata.

## Required-gate results

| Gate | Result | Current measurement |
|---|---:|---|
| Identity policy | Passed | Current-source identity and reasoned compatibility/historical allowlist |
| Release build | Passed | 0 warnings, 0 errors, 41.59 s, 13 projects |
| Security companion | 31/31 passed | Hosted WSC remained `Unknown`; bounded fallback was exercised |
| Supply chain | 18/18 passed | Publisher, signed release state, ZIP staging |
| Security hardening | 12/12 passed | Authenticated state, publisher snapshot, ZIP/workflow boundaries |
| Update planner | 17/17 passed | 2,895.6 ms |
| Device Fabric | 24/24 passed | 47.7 ms |
| Monitoring | 13/13 passed | 765.9 ms |
| WPF controls | 5/5 passed | 1,704.3 ms |
| Six native renders | Passed | Home, Monitoring, Devices, Security, Remote Assist, Updates at 1044×788 |
| Render verification | Passed | Six PNGs present; no `*.error.txt` file |

The representative monitoring observation reported `Partial`, 32 process rows, two ready fixed volumes, two inaccessible processes, 166.6 ms provider time, and 167.1 ms wall time. This is a diagnostic observation on one hosted runner, not a latency or throughput guarantee for another machine.

## Frozen render hashes

| Panel | Bytes | SHA-256 |
|---|---:|---|
| Home | 93,710 | `4732077CA3053349DC359117ECE032B0A5E75313384F61A536C6932887DAB1FC` |
| Monitoring | 102,360 | `F6F6D9C989B0538EA9472B26615CA5CD7A7EAC5CBBC5515D7B907DBBAA326473` |
| Devices | 111,204 | `8B8F9AE4147267D2455B93311CE38004D8F68E59813CEB7D6384FF6A3AD47084` |
| Security | 108,916 | `BD2D700B6DCE5588968A06AF6F54C3B47985DF0E4D32530C625372AAFD70DBBC` |
| Remote Assist | 114,496 | `3F403752B64EE24755B00AB9BDBB1C1787B8FB0A10368A3604D4544950C27E70` |
| Updates | 119,632 | `124F4835984FB0283D0A73312AB43BDE63283B5BACBEA4A55170319EDB9717CC` |

## Failure and recovery evidence

- A pending monitoring sample honors cancellation.
- History capacity and finite-value constraints fail closed.
- Process access failures are counted and do not expose executable paths.
- The application retains only the last confirmed snapshot for two failed retries, marks it stale, then reports unavailable; a later success replaces it and emits one recovery activity entry.
- Window close cancels and awaits the single sampling loop.
- Update acquisition rejection/cancellation cleans only private staging owned by the prepared update.
- Security monitoring retains bounded fallback/recovery behavior when hosted Windows Security Center loading fails.

## Hosted EICAR classification

The opt-in run submitted the standardized harmless EICAR marker to AMSI in memory only. Result: 31/32, with native result `1`; the installed hosted provider did not block it. The workflow correctly classified this as an interoperability gap while keeping required repository-correctness gates green. No live malware, file-based evasion, antivirus exclusion, provider mutation, or independent Soltex detection claim was involved.

## Nonclaims and reopen triggers

This gate does not prove owner-host EICAR behavior, live provider enumeration, antivirus efficacy, production monitoring, benchmark comparability, accessibility conformance, owner visual acceptance, multi-viewport/scaling behavior, production signing, installer/update activation, remote execution, device enrollment, or autonomous correction.

Reopen this gate after any change to monitoring capture/math/bounds, WPF theme/layout/bindings, device observation/capability policy, build/workflow policy, or one of the six rendered pages. Reopen owner-host interoperability and human-acceptance gates only with evidence from the relevant owner-controlled environment.
