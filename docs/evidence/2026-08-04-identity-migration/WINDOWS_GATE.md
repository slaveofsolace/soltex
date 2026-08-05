# Soltex identity migration Windows gate

Date: 2026-08-04
Implementation commit: `42f10865fcbcd31f7b26dbb98446d09cfc69285d`
Stacked base: `40c7e73452dc6c11fcd1f5711ec6360210595a44`
Pull-request merge preview: `54c160942a0f2b0837afaa87ccdd4f7b9aa301d8`

## Environment

- GitHub Actions run `30921441649`
- Job `92032791166`
- Windows Server 2025, build `10.0.26100`
- Runner image `windows-2025-vs2026`, version `20260728.188.1`
- .NET SDK `10.0.302`
- Checkout `D:\a\soltex\soltex`

## Results

| Gate | Result |
| --- | --- |
| Soltex identity policy | Passed; 106 tracked text files, 9 reasoned allowlist entries |
| Release build | Passed; 0 warnings, 0 errors |
| Default security suite | 31/31 passed |
| Supply-chain suite | 18/18 passed |
| Hostile hardening suite | 12/12 passed |
| Update-planner suite | 17/17 passed; 2,816.4 ms |
| Device Fabric suite | 20/20 passed; 41.8 ms |
| Security native render | Passed |
| Remote Assist native render | Passed |
| Updates native render | Passed |
| Render artifact verification | Passed; no render-error files |
| Opt-in EICAR interoperability | 31/32; hosted AMSI provider returned native result 1 |

The hosted image did not expose a usable `wscapi.dll` provider boundary. The observed provider-neutral path therefore demonstrates bounded `Unknown` fallback behavior on that runner, not successful live provider enumeration. The EICAR result describes the installed hosted AMSI provider and does not measure Soltex detection efficacy.

## Retained artifact

- Name: `soltex-windows-evidence-30921441649-1`
- Artifact ID: `8897293182`
- ZIP size: 323,918 bytes
- SHA-256: `ea7781b0296147362d4546abe5076ec0282f0f15f30256ebb3f5d4961f5f6195`
- Retention: 30 days
- Contents: build, identity, gate-classification and suite logs; native Security, Remote Assist, and Updates PNGs

The artifact was fetched through the authenticated GitHub connector, its ZIP digest was independently recomputed as the same SHA-256, its paths were checked before extraction, and the three PNGs were visually inspected. That inspection found the Soltex identity present and the existing warm editorial layout intact. It is not owner visual acceptance, a full viewport matrix, or accessibility conformance.
