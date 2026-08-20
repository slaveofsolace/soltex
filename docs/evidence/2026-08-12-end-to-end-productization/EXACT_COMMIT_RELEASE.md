# Exact-commit release evidence

Date: 2026-08-13

Source commit: `48e806092cb160e9edaef09e06552dfdbfea0530`

## Native product matrix

`eng/capture-ui-evidence.ps1` captured and validated all 18 native WPF states
at 1280x820 with source and tested commit both bound to `48e8060`.

- Manifest: `<local-evidence-root>\2026\08\13\soltex-product-rebuild\48e8060-native-validation\render-matrix.json`
- Manifest bytes: 8,274
- Manifest SHA-256: `76F2DCC6C423822E358D8447D7C8716009CC5FA23614A30035C35C21AE7F3CF7`
- Directly inspected: Overview, Quick benchmark, Audio, Security, Remote Assist,
  Activity, and the packaged Overview state.
- Result: no exception dialog, error sidecar, clipping, broken graphic, or
  information-density blocker was observed. This is agent inspection; owner
  visual acceptance remains open.

## Runtime lifecycle

The exact-commit runtime probe passed with report SHA-256
`4A0F64051B04D0F84B26239A0BB8239E0DDE86AE06F81E63AF1B22EDB61832BE`.

| Signal | Observed |
|---|---:|
| Startup completion | 804.8471 ms |
| Navigation | 18 transitions; 19.2075 ms mean; 241.422 ms maximum |
| Visible idle CPU | 0.6211% normalized |
| Minimized transition CPU | 2.2493% normalized |
| Minimized steady CPU | 0% normalized |
| Hidden notification-area CPU | 0% normalized |

Performance sampling was active only in visible idle and stopped for minimized
and hidden states. The controlled process exited normally.

## Package and installer

| Artifact | Bytes | SHA-256 | Authenticode |
|---|---:|---|---|
| Self-contained `Soltex.exe` | 71,621,929 | `88278BDC266630D1AC0FE4D4C93FD9CDD3B380B12FAC94DB4E010E177E15F42F` | `NotSigned` |
| `Soltex-1.0.2-win-x64-setup.exe` | 66,088,566 | `372703A9237D46DFA911421E258C406D0F86262478158E832BF679FA2A7723DA` | `NotSigned` |

The self-contained executable rendered Home and exited `0`. Package identity
schema 2 binds the executable and its 1280x820 render to source/tested commit
`48e8060`; the identity file is 937 bytes with SHA-256
`745BFA230C427030FDDAE03AEC46601CD5252E63E3933BCC9FA38F9C864E30FA`.
The retained Home PNG is 91,863 bytes with SHA-256
`CBB7498E83BF7577F1BF02A49F539A8239FCBDF588010716E39A6D78E823E69B`.

The `1.0.2` installer compiled successfully but was not installed. The first
installer-only launch attempt returned host error `Access is denied` before a
result could be recorded. A bounded reconciliation confirmed no installer or
Soltex process, registry display version still `1.0.0`, and installed executable
SHA-256 still `DA2F08A640C2D3D6E9B857BDCB724062A3D61AEBE9D2AE5B88C95E296A93F5FD`.
Therefore upgrade and immediate pre/post state preservation are not claimed for
this commit.

## Defensive-hardening conclusion and nonclaims

Defensive hardening was applied to bounded benchmark-result loading, private
per-run scratch ownership, exact-length reads, identity-aware cleanup, and
visibility-bound cancellation. Real-world exploitability of the two filesystem
race hypotheses was not dynamically established because policy-safe validation
was intentionally not attempted.

This evidence does not establish signed/trusted distribution, installer upgrade
or rollback, hosted CI acceptance, owner visual acceptance, GPU/thermal/stability
benchmarking, system-wide antivirus replacement, or SteelSeries Sonar parity.
