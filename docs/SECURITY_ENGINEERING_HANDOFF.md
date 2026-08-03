# WaveSlate Security engineering handoff

## Outcome

The implemented product is a lightweight **security companion**, not a new antivirus engine. It uses the protection already registered with Windows and adds controls around WaveSlate's own trust boundaries.

This gives the application strong practical coverage without adding a second file-system filter, resident scanner, or cloud telemetry pipeline:

- Windows Defender or another registered provider supplies system-wide real-time protection.
- Windows Security Center supplies provider-neutral aggregate health.
- Defender's supported PowerShell module supplies Defender-specific status, quick/custom scans, and intelligence updates.
- AMSI lets WaveSlate submit content before the app consumes it.
- WaveSlate verifies its packages and state independently with hashes, signatures, strict paths, and authenticated metadata.

## Implemented components

| Component | File | Behavior |
| --- | --- | --- |
| Provider health | `WindowsSecurityCenter.cs` | Calls `WscGetSecurityProviderHealth` for antivirus health. |
| Native import policy | `NativeImportPolicy.cs` | Constrains all security-assembly P/Invokes to System32 to reduce DLL preloading risk. |
| Provider change signal | `WindowsSecurityChangeMonitor.cs` | Uses `WscRegisterForChanges`; disposal unregisters the native callback. |
| Protection monitor | `ProtectionMonitor.cs` | Serializes refreshes, coalesces WSC/manual signals, cancels each expired polling wait so no abandoned reader can consume a future signal, keeps last-known-good state, backs off after failures, and reports recovery. |
| Defender orchestration | `PowerShellDefenderClient.cs` | Launches the System32 host; imports Defender, Diagnostics, and Utility by direct `$PSHOME` manifest paths; module-qualifies fixed commands; passes custom paths through a process environment variable; byte-bounds redirected output while reading; and observes I/O cleanup on cancellation. |
| Defender event correlation | `PowerShellDefenderClient.cs`, `DefenderEventLogParser.cs` | Reads a bounded allow-list of Operational events under count/time/byte ceilings and emits safe descriptions without raw resource paths. |
| AMSI | `AmsiContentScanner.cs` | Maps clean, policy-blocked, malware, unavailable, and error outcomes. |
| Intake assessment | `FileAssessmentService.cs` | Rejects reparse points, computes SHA-256, enforces a 16 MiB AMSI budget, and flags executable content for signature policy. |
| Authenticode | `AuthenticodeVerifier.cs` | Uses `WinVerifyTrust`, no UI, whole-chain policy, cached revocation by default, and MD2/MD4 rejection. |
| Detached manifests | `IntegrityManifestVerifier.cs` | Verifies RSA-PSS/SHA-256 over exact JSON bytes, then validates schema, root containment, size, and SHA-256. |
| Allow list | `AllowListStore.cs` | Stores a maximum of 512 exact SHA-256 entries in authenticated state. |
| Quarantine | `QuarantineStore.cs` | Moves or verified-copies payloads, authenticates the index, re-hashes before restore, and avoids overwrite collisions. |
| Audit | `SecurityAuditLog.cs` | HMACs paths, chains entries, bounds fields, and rotates at 4 MiB. |
| Import guard | `ImportFolderMonitor.cs` | Watches only WaveSlate Imports with a bounded queue and debounce. |
| UI | `MainWindow.xaml` | Shows health, focused scans, updates, quarantine, activity, and explicit scope disclosure. |

## Defender and Windows boundaries

Microsoft documents Defender Antivirus as layered real-time, behavioral, heuristic, local-intelligence, and cloud-delivered protection. WaveSlate reads status; it does not reproduce those detection systems. Supported commands used here are `Get-MpComputerStatus`, `Get-MpPreference`, `Start-MpScan`, and `Update-MpSignature`.

AMSI is an application intake interface. A clean AMSI result means the installed provider did not detect that submitted buffer; it is not a universal trust assertion. WaveSlate therefore keeps executable content in `ReviewRecommended` until release-signature policy also succeeds.

Windows Security Center client APIs report provider health. `WscRegisterForChanges` signals that a re-query is needed; the callback itself is not a health verdict. Calling either API does not register WaveSlate as an antivirus provider. SmartScreen is also a separate Windows/Edge reputation boundary; WaveSlate does not claim to query or reproduce a SmartScreen reputation score.

Defender `AMRunningMode` is surfaced as reported (`Normal`, `Passive`, or `EDR Block Mode`) without changing it. Explicit WSC aggregate states take precedence over Defender detail flags; only an unavailable WSC result permits a `Normal` and active Defender status to serve as a disclosed fallback. Passive Defender never substitutes for unknown registered-provider health. The event reader uses Microsoft's documented Defender Operational log and event IDs. It does not read arbitrary endpoint telemetry, and it intentionally excludes raw `Path`, `Process Name`, and `Scan Resources` values from UI models.

## Malwarebytes clean-room findings

Malwarebytes publicly describes layered web, malware, ransomware, and exploit protection, along with scans, quarantine, restore/delete, and allow lists. The safe product patterns adopted here are generic security UX patterns:

- visible layer health;
- focused scan scopes;
- recoverable quarantine;
- deliberate allow-list changes;
- update and package integrity;
- clear detection history and false-positive recovery.

Do not copy Malwarebytes code, names, icons, layouts, threat taxonomy, signatures, machine-learning models, endpoints, driver design, protocols, or protected internals. WaveSlate's implementation is independently written against Windows documentation.

Malwarebytes also documents optional Windows Security Center registration and possible conflicts when multiple security products operate together. WaveSlate does not toggle that registration, add mutual exclusions, disable any layer, or assume coexistence is conflict-free. Provider ownership remains visible through Windows and vendor-owned settings.

## False-positive policy

- AMSI malware or administrator-policy blocks may prevent an import.
- An unknown or unsigned executable is **review required**, not automatically malware.
- A user may add only an exact SHA-256 to the WaveSlate allow list.
- Restore requires a warning and never automatically creates an allow entry.
- WaveSlate does not add Microsoft Defender exclusions.
- High-impact automatic remediation is intentionally absent from the MVP.

## Privacy policy

- No file, path, hash, or sample is uploaded by WaveSlate.
- Defender may use Microsoft's cloud-delivered protection according to the user's Windows policy; WaveSlate only reports whether Defender exposes that layer as active.
- Audit records retain an HMAC of paths rather than raw paths.
- PowerShell stdout/stderr is byte-bounded while it is read; excess is drained and discarded, overflow is rejected, and no already-unbounded `ReadToEndAsync` string enters application state.
- Future cloud reputation or sample submission requires separate consent, retention, deletion, authentication, and abuse controls.

## What is still required for a true antivirus product

The following are not implemented and must not be claimed:

- pre-open file enforcement through a production minifilter;
- process, memory, exploit, web, ransomware, or EDR sensors;
- ELAM boot driver and protected antimalware service/PPL;
- Windows Security Center antivirus-provider registration;
- proprietary signatures, classifiers, cloud reputation, or sample detonation;
- tamper-resistant updates and service recovery at antivirus-vendor level;
- Microsoft Virus Initiative membership, Trusted Signing, WHQL/HLK, or independent efficacy certification;
- measured malware detection, protection, remediation, or false-positive rates.

Microsoft's current MVI criteria require a commercially available real-time antimalware product, Windows compatibility/update responsibility, Trusted Signing, maintained independent certification, and program agreements. ELAM participation also depends on MVI and WHQL prerequisites.

## Next implementation stages

1. Define the release publisher and certificate/thumbprint policy.
2. Add monotonic release sequence and anti-rollback state to signed manifests.
3. Gate every plugin/update staging operation on AMSI, Authenticode publisher policy, detached manifest, schema, and hash checks.
4. Add optional WSC product-name inventory only if it can remain provider-neutral and avoid unsupported registration behavior.
5. Add archive extraction in a bounded staging directory with entry-count, size, ratio, traversal, and reparse defenses.
6. Sign release artifacts and verify clean install, update, rollback, recovery, and uninstall.
7. Run independent performance, coexistence, accessibility, and false-positive testing.

## Primary references

- [Microsoft Defender Antivirus protection layers](https://learn.microsoft.com/en-us/windows/security/book/operating-system-security-virus-and-threat-protection)
- [Defender PowerShell module](https://learn.microsoft.com/en-us/powershell/module/defender/)
- [Antimalware Scan Interface reference](https://learn.microsoft.com/en-us/windows/win32/amsi/antimalware-scan-interface-reference)
- [How AMSI helps applications](https://learn.microsoft.com/en-us/windows/win32/amsi/how-amsi-helps)
- [Windows Security architecture](https://learn.microsoft.com/en-us/windows/security/operating-system-security/system-security/windows-defender-security-center/windows-defender-security-center)
- [WSC aggregate provider health](https://learn.microsoft.com/en-us/windows/win32/api/wscapi/nf-wscapi-wscgetsecurityproviderhealth)
- [WSC change registration](https://learn.microsoft.com/en-us/windows/win32/api/wscapi/nf-wscapi-wscregisterforchanges)
- [Defender Operational event IDs](https://learn.microsoft.com/en-us/defender-endpoint/troubleshoot-microsoft-defender-antivirus)
- [Defender compatibility with other security products](https://learn.microsoft.com/en-us/defender-endpoint/microsoft-defender-antivirus-compatibility)
- [WinVerifyTrust data and revocation flags](https://learn.microsoft.com/en-us/windows/win32/api/wintrust/ns-wintrust-wintrust_data)
- [Microsoft Virus Initiative criteria](https://learn.microsoft.com/en-us/unified-secops/virus-initiative-criteria)
- [ELAM prerequisites](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/elam-prerequisites)
- [Malwarebytes real-time protection layers](https://help.malwarebytes.com/hc/en-us/articles/31589448817563-Turn-on-Real-Time-Protection-in-Malwarebytes-for-Windows-and-Mac)
- [Malwarebytes quarantine behavior](https://help.malwarebytes.com/hc/en-us/articles/31589479169179-Manage-quarantined-items-in-Windows-and-Mac)
- [Malwarebytes allow-list behavior](https://help.malwarebytes.com/hc/en-us/articles/31589453538075-Allow-or-block-items-using-Malwarebytes-Allow-list)
- [Malwarebytes Windows Security Center setting](https://help.malwarebytes.com/hc/en-us/articles/31589223674907-Manage-General-settings-in-Malwarebytes-for-Windows)
- [Malwarebytes and other antivirus products](https://help.malwarebytes.com/hc/en-us/articles/31589229637275-Malwarebytes-and-other-antivirus-software)
