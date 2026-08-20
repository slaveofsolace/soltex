# Stale-install quarantine lifecycle recovery

Date: 2026-08-12

## Incident attribution

The owner reported this installed-app dialog:

```text
Cannot access a disposed object.
Object name: 'Soltex.Security.QuarantineStore'.
```

The Start Menu shortcut targeted
`C:\Users\suhai\AppData\Local\Programs\Soltex\Soltex.exe`. That executable was
71,479,485 bytes, SHA-256
`815762A7D57EDE5CD0C5C59F72A29A85DAD8698EB06E34746C29AD65B778CD31`, and
reported product version `1.0.0+8a7b4a76ec4fd262216d78a055d72ff48f1c3da2`.
Commit `8a7b4a76` was created on August 5 and is an ancestor of the lifecycle fix at
`4207ecb`; the installed copy was therefore stale.

The corrected lifecycle cancels and drains startup, active operation, and
telemetry work before disposing the security runtime. A drain miss reports
`ResourcesDisposed=false` and cannot produce a successful controlled exit.
`TelemetryLoopOwner` separately serializes visibility stop/restart ownership so
no second path can retain and dispose the same cancellation source.

## Exact replacement identity

Source commit: `d3429777c7baff67f08f1e78cc5540d71f58da03`

| Artifact | Bytes | SHA-256 | Authenticode |
|---|---:|---|---|
| Published `Soltex.exe` | 71,589,691 | `ECBE0D7CD3E700B0CE808B074490C3B5255433336D79DF7EACF13C2DE6FA26F8` | `NotSigned` |
| `Soltex-1.0.1-win-x64-setup.exe` | 66,057,630 | `FEC07112FEF7D6B69376418CCA5E02986FDFB4A8C2DBBA7969DB3D7CFF90810E` | `NotSigned` |
| Installed `Soltex.exe` | 71,589,691 | `ECBE0D7CD3E700B0CE808B074490C3B5255433336D79DF7EACF13C2DE6FA26F8` | `NotSigned` |

The per-user installer exited `0`, retained the same AppId and Start Menu target,
and registered display version `1.0.1`. The installed executable reports product
version `1.0.0+d3429777c7baff67f08f1e78cc5540d71f58da03`; the installer display version
and assembly product version are recorded separately rather than represented as
identical.

## State preservation

The compatibility resolver selected the existing
`%LOCALAPPDATA%\WaveSlate` data root because no competing canonical root exists.
Immediately before and after the upgrade, the two existing files retained exact
length, modification time, and SHA-256:

| Relative file | Bytes | SHA-256 |
|---|---:|---|
| `Security\events.jsonl` | 153,469 | `36F71B554A023A297B9F60B1929E6B30E0AFFDA9DF2105B778364BC603EDA60B` |
| `Security\state\state.key` | 320 | `F7AEE1E29ED80F57F3305A98C1BA2FA9A8684C33E5768E897DC3E24A38485C9D` |

Only hashes and metadata were inspected; key or audit contents were not exposed.
Later verification launches may append normal local audit events and are not part
of the immediate pre/post preservation comparison.

## Installed lifecycle gate

Three independent installed Security initialize/render/shutdown cycles completed
at 1280 by 820. Every cycle exited `0`, produced no `.error.txt` sidecar, and
left no Soltex process:

| Cycle | PNG bytes | SHA-256 |
|---:|---:|---|
| 1 | 101,123 | `3E1C17DD32DBF982840EF4B159DB88EF4A71832284E9B3A66216E654985DBBFC` |
| 2 | 100,851 | `1F86DF7548FD99114A41891A2C32C8A584936D51F8A9E05D75283E271AFFA52A` |
| 3 | 100,769 | `7DC6EF0EA8E38523110CC34623B1468F5DB1A8FF9D4B324CB9FD330F71BD1E06` |

External evidence roots:

```text
<local-evidence-root>\2026\08\12\soltex-product-rebuild\package-d342977-20260812-0320
<local-evidence-root>\2026\08\12\soltex-product-rebuild\installed-recovery-d342977-20260812-0324
```

## Nonclaims

- The package and executable remain unsigned and are not a public trusted
  release.
- Three controlled shutdowns plus focused lifecycle tests address the reported
  regression but do not prove that no lifecycle defect can exist.
- This recovery does not make Soltex an antivirus provider or establish malware
  detection efficacy.
- Owner visual acceptance and broader scaling/accessibility gates remain open.
