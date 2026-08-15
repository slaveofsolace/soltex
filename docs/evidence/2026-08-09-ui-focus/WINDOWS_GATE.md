# Soltex UI-focus Windows gate

Date: 2026-08-09

Evidence class: owner-host candidate verification

Branch: `soltex-ui-focus-v1`

Base: `a0daa2163529d8f813e79ea7e342ddfbd5c61f50`

Runtime: Microsoft .NET SDK 10.0.302, task-local; native WPF; Windows 10.0.26200 x64

## Required results

| Gate | Result | Current evidence |
|---|---:|---|
| Release build | Passed | 0 warnings, 0 errors, 13 projects |
| Identity policy | Passed | 155 tracked text files, 9 reasoned allowlist entries |
| Design-token policy | Passed | 7 XAML files; colour literals confined to `Tokens.xaml` |
| Security and Remote Assist | 32/32 | Includes bounded live WSC/Defender observations and opt-in in-memory EICAR/AMSI |
| Monitoring | 15/15 | 1,022.2 ms; live observation remained bounded and provenance-labelled |
| WPF application | 9/9 | 531.8 ms; includes Monitoring and Mixer disclosure contracts |
| Core Audio | 10/10 | 941.8 ms; live endpoint capture and overhead measurement |
| Device Fabric | 24/24 | 60.8 ms |
| Security hardening | 12/12 | Authenticated state, ZIP, publisher, and workflow boundaries |
| Supply chain | 18/18 | Publisher, signed-state, and bounded staging contracts |
| Update planner | 17/17 | 1,735.6 ms |
| Native default renders | Passed | Eight 1280x820 PNGs |
| Native expanded renders | Passed | Monitoring details, Mixer more endpoints, Security activity |
| Static render repeatability | Passed | Three independent Clips launches produced one exact SHA-256 |

Total focused executable checks: 137/137 passed.

## Exact command boundary

The owner-host Release and opt-in interoperability gate ran once after repairing its Windows PowerShell 5.1 compatibility:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\verify.ps1 -RunEicar
```

The remaining Audio, Device Fabric, Security Hardening, Security Supply Chain, and Update test executables ran independently with `dotnet run --configuration Release --no-build`. The identity and design-token policies were also run directly through Windows PowerShell 5.1.

## Representative measurements

- Windows Security Center health query: 696.2 ms on this observation.
- Defender Operational event query: 16 events in 282.3 ms.
- Security observation micro-measurement: WSC mean 0.520 ms; AMSI 4 KiB mean 1.511 ms.
- Monitoring observation: `Partial`, 32 bounded process rows, 3 volumes, 1 interface, 256 inaccessible observations, 184.6 ms provider time and 184.9 ms wall time.
- Core Audio observation: `Current`, 31 render endpoints, 22 capture endpoints, 0 inaccessible, 314.3 ms provider time and 314.7 ms wall time.

These measurements describe one owner-host run. They are diagnostic evidence, not latency guarantees or cross-machine benchmark results.

## Failure and recovery evidence

- The native render path now selects software rendering before WPF font resources initialize, warms the surface, and flushes render-priority work before encoding.
- Three independent Clips renders were byte-identical: 67,890 bytes, SHA-256 `fc0d0b83d6737bb98b1b7cd27aca6586779b7b6384d0673e5b958bfefe36f12c`.
- Live telemetry pages are not expected to hash identically because their observed values and timestamps legitimately change.
- Mixer unavailable-state handling clears every primary and additional list rather than retaining stale endpoints.
- Defender event-query failure clears and collapses the activity table, disables its disclosure control, and reports unavailability.
- The identity verifier replaced a .NET overload absent from Windows PowerShell 5.1; the design-token verifier replaced a non-ASCII parser hazard. Both now pass through stock Windows PowerShell.

## EICAR/AMSI classification

The opt-in test assembled the standardized harmless EICAR marker in memory and submitted those bytes to the installed AMSI provider. The provider returned a blocking result and the test passed. The marker was not written as an EICAR file.

This confirms one interoperability path on this host. It does not establish malware-detection efficacy, independent Soltex detection, file-system interception, provider registration, cloud reputation, or production antivirus status. Soltex did not disable Defender, add an exclusion, or replace the registered provider.

## Open gates

- GitHub CI and package-smoke must pass on the exact published candidate head.
- Owner visual acceptance remains separate from this agent review.
- Keyboard-only, screen-reader, contrast-ratio, high-contrast, reduced-motion, localization, DPI, and multi-viewport matrices remain open.
- Signed distribution, installer lifecycle, and production trust roots remain unconfigured.
