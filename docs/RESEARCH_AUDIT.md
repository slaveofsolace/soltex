# Research audit

## Sources and method

Research used public first-party documentation from Microsoft, Malwarebytes, RustDesk, and Zen Browser. No binaries were disassembled, no protected endpoints were queried, no signatures/models were acquired, and no private implementation was inferred as fact.

Key conclusions:

- Defender is a layered Windows protection system; WaveSlate should cooperate with it rather than duplicate it.
- AMSI is appropriate at an application content-intake boundary.
- WSC client APIs report protection state but do not grant antivirus-provider registration.
- `WscRegisterForChanges` is a supported lightweight signal that provider health may have changed; WaveSlate still re-queries health rather than treating a callback as a verdict.
- Defender's Operational log is the supported local source used for scan, detection, action, failure, recovery, configuration, and tamper events. WaveSlate reads an allow-listed, bounded subset and removes resource paths.
- SmartScreen reputation is a separate boundary and is not exposed as a generic WaveSlate score.
- Generic security-product UX patterns such as layer health, scans, quarantine, restore/delete, and exact allow decisions can be independently implemented.
- True third-party antivirus status requires organizational, signing, platform, compatibility, update, and independent-testing commitments beyond an app feature.
- RustDesk separates screen capture, input, clipboard, client, server, and rendezvous responsibilities, but its AGPL-3.0 license is incompatible with silently copying that implementation into a closed WaveSlate executable. WaveSlate therefore uses a clean external-process boundary.
- Zen Browser's compact mode, split-view emphasis, transient Glance layer, warm dark palette, and editorial hierarchy are product-design references only. WaveSlate uses independently authored WPF layout and no Zen assets, branding, or source.

The external ChatGPT handoff requested by the owner was sent to conversation `6a6f4dbd-e6ec-83ea-b36e-cdb4207a247c` and completed. Its recommendation matched the companion-versus-provider split implemented here. Its sandbox files were not imported; this repository contains an independently written, source-matched handoff.

## Defender and Malwarebytes interoperability

These products are not interchangeable implementation templates:

- Windows Security Center owns the provider-registration and aggregate-health view. WaveSlate only observes it.
- Microsoft documents Defender as active, passive/EDR block, or disabled depending on Windows edition, Defender for Endpoint onboarding, Smart App Control, and whether a non-Microsoft antivirus is registered. WaveSlate now reports `AMRunningMode` but never changes the mode.
- Malwarebytes documents an optional Windows Security Center registration setting. When enabled, Windows recognizes Malwarebytes as the security solution. That setting belongs to Malwarebytes and the user; WaveSlate does not toggle it.
- Malwarebytes also documents that multiple security products can conflict, including web-filtering conflicts on Windows Filtering Platform. WaveSlate therefore does not create exclusions, stop services, or promise that two real-time products are always safe together.
- Generic product lessons retained are visible layer health, bounded history, explicit scans, recoverable quarantine, deliberate exact-hash exceptions, failure visibility, and recovery confirmation. No Malwarebytes driver, signature, model, endpoint, protocol, asset, name, or layout was acquired or copied.

Primary sources for this slice:

- https://learn.microsoft.com/en-us/windows/win32/api/wscapi/nf-wscapi-wscgetsecurityproviderhealth
- https://learn.microsoft.com/en-us/windows/win32/api/wscapi/nf-wscapi-wscregisterforchanges
- https://learn.microsoft.com/en-us/windows/win32/amsi/antimalware-scan-interface-portal
- https://learn.microsoft.com/en-us/defender-endpoint/troubleshoot-microsoft-defender-antivirus
- https://learn.microsoft.com/en-us/defender-endpoint/microsoft-defender-antivirus-compatibility
- https://help.malwarebytes.com/hc/en-us/articles/31589223674907-Manage-General-settings-in-Malwarebytes-for-Windows
- https://help.malwarebytes.com/hc/en-us/articles/31589229637275-Malwarebytes-and-other-antivirus-software
- https://help.malwarebytes.com/hc/en-us/articles/36231156084891-Windows-Filtering-Protection-conflict-with-other-antivirus-software

See `docs/SECURITY_ENGINEERING_HANDOFF.md` for direct documentation links.

## RustDesk and Zen reference slice

The official RustDesk 1.4.7 tag archive was downloaded for source review and extracted without links outside the repository at `C:\Users\suhai\Documents\WaveSlate References\RustDesk\1.4.7`. Its archive SHA-256 is `895030877bc23e2902c6c560cacff17eafefb77852042c501ef0e6c3c2fa1574`. Nothing from that tree was built, executed, or copied into WaveSlate.

Primary sources:

- https://github.com/rustdesk/rustdesk
- https://github.com/rustdesk/rustdesk/blob/1.4.7/LICENCE
- https://rustdesk.com/docs/en/dev/build/
- https://rustdesk.com/docs/en/client/windows/windows-portable-elevation/
- https://zen-browser.app/
- https://docs.zen-browser.app/user-manual/compact-mode
- https://docs.zen-browser.app/user-manual/split-view
- https://docs.zen-browser.app/user-manual/glance

See `docs/REMOTE_ASSIST.md` for the resulting license, trust, failure, and nonclaim boundary.

## PowerShell child and error-boundary follow-up

The later source hardening uses two additional documented platform contracts:

- With `UseShellExecute=false`, `ProcessStartInfo.WorkingDirectory` becomes the started process's working directory and is not used to locate the executable. WaveSlate therefore keeps the executable path absolute and anchors the security child to its executable directory.
- PowerShell `-ErrorAction Stop` promotes non-terminating cmdlet errors so `try`/`catch` can distinguish the expected no-matching-events case from provider, log, or access failures. WaveSlate does not surface raw stderr because it can contain user-selected paths.

Primary sources:

- https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.workingdirectory?view=net-10.0
- https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_error_handling?view=powershell-7.6
