# Reference-system research evidence

Date: 2026-08-04

Method: public first-party metadata and documentation review

Resource Pilfer disposition: **REFERENCE ONLY**

## Scope

The pass covered AppControl, AppControl's separate MCP repository, Zen Browser, NZXT CAM, SteelSeries Sonar, RustDesk, Tailscale, Microsoft Defender, Malwarebytes, Google Drive, Box, and Microsoft's Windows Core Audio documentation.

The independently authored outcome is [`../../REFERENCE_SYSTEMS.md`](../../REFERENCE_SYSTEMS.md). The fail-closed rights record for the only proposed proprietary binary acquisition is [`CANDIDATE_REGISTRY.json`](CANDIDATE_REGISTRY.json).

## Acquisition decision

- AppControl desktop installer: **not downloaded and not executed**. Public behavior, privacy, version, and terms pages answered the current requirement questions; the desktop EULA reserves proprietary rights and adds no clean-room value that would justify binary acquisition.
- AppControl MCP: public repository metadata and license reviewed; no clone, release asset, package, or source file acquired. The official repository states that the MCP bridge is MIT, read-only, and separate from the proprietary desktop application.
- Other systems: official public metadata/documentation only. No installer, archive, repository, image, font, icon, design token, DSP preset, detection signature/model, private API, or active content was acquired.

## Official sources checked

- <https://www.appcontrol.com/>
- <https://www.appcontrol.com/privacy/>
- <https://www.appcontrol.com/eula/>
- <https://www.appcontrol.com/changelog/>
- <https://www.appcontrol.com/mcp/>
- <https://github.com/AppControlLabs/appcontrol-mcp-go/>
- <https://zen-browser.app/>
- <https://docs.zen-browser.app/user-manual/compact-mode>
- <https://docs.zen-browser.app/user-manual/workspaces>
- <https://nzxt.com/pages/cam>
- <https://steelseries.com/en-gb/gg/sonar>
- <https://github.com/rustdesk/rustdesk>
- <https://tailscale.com/docs/concepts/wireguard>
- <https://learn.microsoft.com/en-us/defender-endpoint/microsoft-defender-security-center-antivirus>
- <https://help.malwarebytes.com/hc/en-us/article_attachments/49551606875547>
- <https://developers.google.com/workspace/drive/api/guides/about-sdk>
- <https://developers.google.com/identity/protocols/oauth2/scopes>
- <https://developer.box.com/guides/api-calls/permissions-and-errors/scopes>
- <https://learn.microsoft.com/en-us/windows/win32/coreaudio/about-the-windows-core-audio-apis>

## Observed facts

- AppControl advertised Windows version `1.4.0.415` and a 20.2 MB download. Its July 26, 2026 changelog entry identifies that version.
- AppControl publicly describes historical resource monitoring, privacy and install events, alerts, process controls, and an optional AI/MCP integration.
- AppControl says suspicious-app analysis and MCP are off by default. Its privacy page describes the executable filename/hash/publisher fields sent when the optional suspicious-app feature is enabled.
- AppControl's terms warn that kill/disable actions can impair Windows or remote access and that executable insights can be incomplete or inaccurate.
- The public AppControl MCP repository identifies an MIT-licensed, nine-tool, read-only bridge; it also states that the desktop application remains proprietary.

## Nonclaims

No runtime, installer, code, network, performance, security, detection, UI-parity, or interoperability inspection was performed on AppControl or the other reference products. Page availability and claims are controlled by their publishers and may change after this snapshot.

Later evidence is kept separate: a user-authorized, local, visual-only SteelSeries GG/Sonar observation from 2026-08-09 is recorded in [`../2026-08-09-ui-evidence-matrix/RESOURCE_PROVENANCE.md`](../2026-08-09-ui-evidence-matrix/RESOURCE_PROVENANCE.md). It does not retroactively change this public-metadata snapshot.
