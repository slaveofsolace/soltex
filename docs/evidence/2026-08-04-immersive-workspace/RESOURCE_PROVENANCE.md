# Resource provenance and clean-room disposition

Date checked: 2026-08-04

Decision: **REFERENCE ONLY**

## Candidate and current stage

| Reference | Canonical source | Stage | Disposition |
|---|---|---|---|
| Zen Browser public product direction | [zen-browser.app](https://zen-browser.app/) | Metadata/reference page inspected; no acquisition | REFERENCE ONLY |
| RustDesk public source project | [rustdesk/rustdesk](https://github.com/rustdesk/rustdesk) and [license](https://github.com/rustdesk/rustdesk/blob/master/LICENCE) | Repository metadata/license inspected; no acquisition in this slice | REFERENCE ONLY / external program |
| Windows CPU timing contract | [GetSystemTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes) | Primary API documentation | Cited implementation input |
| Windows physical-memory contract | [GlobalMemoryStatusEx](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex) | Primary API documentation | Cited implementation input |

## Principles retained

From Zen Browser's public product page, Soltex retained only general principles: a calmer workspace, compact navigation, separable workspaces, focus, and an explicit balance among beauty, performance, and privacy. No distinctive layout, measurement, source, asset, trademark, icon, screenshot, font, animation, or design token was copied.

From the RustDesk public project, Soltex retained only the architectural boundary that remote desktop is a separately operated external client and visible remote-hands fallback. The official repository identifies the project as AGPL-3.0 and exposes screen capture, transport, input, and session behavior outside Soltex. Soltex therefore keeps RustDesk out of the proprietary runtime and implements only explicit executable selection, Windows trust/hash revalidation, fixed shell-free arguments, visible confirmation, and peer-ID-free auditing.

Microsoft's primary API documentation supports the bounded provider choices. Soltex calls `GetSystemTimes` for aggregate CPU timing and `GlobalMemoryStatusEx` for physical-memory state, then labels unsupported GPU/network signals unavailable instead of deriving them from unrelated counters.

## Acquisition and payload record

- No Zen repository, image, executable, font, icon, screenshot, or UI asset was downloaded or imported for this implementation.
- No RustDesk repository, archive, submodule, library, executable, service, installer, UI asset, or source file was downloaded or imported into this task worktree.
- No bundled executable, macro, plugin, installer, or third-party script was run.
- No authenticated entitlement or account mutation occurred.
- No third-party payload was submitted to an external generative or conversion service.
- There is therefore no acquired archive hash or runtime budget to approve in this slice.

## Hard gates and nonclaims

Public visibility and open-source status do not by themselves authorize relabeling or proprietary embedding. The RustDesk repository's AGPL-3.0 label is sufficient reason to retain the existing external-process boundary; this record is operational provenance, not legal advice. Any future source reuse, fork, bundled distribution, self-hosted server, or direct protocol integration requires a new exact-version license/dependency/security review and a separately authorized acquisition stage.

This disposition does not call Soltex affiliated with, endorsed by, derived from, or visually accepted by Zen Browser or RustDesk. It does not approve a production RustDesk deployment, unattended access, public ingress, credential handling, or remote-control security.
