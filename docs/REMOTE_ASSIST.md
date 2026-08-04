# Remote Assist boundary

## Decision

Soltex does not copy, link, or bundle RustDesk code into the proprietary application. RustDesk is licensed under AGPL-3.0, including the network-source obligations in section 13. The current implementation therefore treats a separately installed RustDesk client as an external program with its own user interface, transport, authentication, consent, updates, and session lifecycle.

Official references:

- [RustDesk repository and architecture](https://github.com/rustdesk/rustdesk)
- [RustDesk AGPL-3.0 license](https://github.com/rustdesk/rustdesk/blob/1.4.7/LICENCE)
- [RustDesk build documentation](https://rustdesk.com/docs/en/dev/build/)
- [Windows portable elevation behavior](https://rustdesk.com/docs/en/client/windows/windows-portable-elevation/)

## Research provenance

The unmodified RustDesk 1.4.7 source archive was cached outside the Soltex repository for clean-room architectural study only:

- Archive: `C:\Users\suhai\Documents\WaveSlate References\RustDesk\1.4.7\rustdesk-1.4.7.tar.gz`
- Extracted reference: `C:\Users\suhai\Documents\WaveSlate References\RustDesk\1.4.7\source`
- SHA-256: `895030877bc23e2902c6c560cacff17eafefb77852042c501ef0e6c3c2fa1574`
- Files/directories inspected after safe extraction: 931 files and 178 directories; links were not extracted.

Architectural lessons used independently are separation of capture/input/clipboard/session responsibilities, a visible client-owned session surface, and a narrow connect boundary. No RustDesk source, Flutter UI, protocol implementation, branding, assets, or compiled binary was moved into this repository.

## Implemented current-source slice

`src/Soltex.RemoteAssist` provides a small external-process adapter:

- discovery is bounded to `RustDesk\RustDesk.exe` under the two Windows Program Files roots;
- a manually selected executable must exist, have the exact filename, and not be a reparse point;
- the selected bytes receive a SHA-256 approval fingerprint and are rechecked immediately before launch;
- the selected path and full approval fingerprint are visible in the Remote Assist page;
- the WPF app requires Windows Authenticode trust and rechecks it for every launch request;
- sharing opens RustDesk with no arguments;
- control accepts only a 3–64 character peer ID containing ASCII letters, numbers, `-`, or `_`;
- control uses `ProcessStartInfo.ArgumentList` with the fixed pair `--connect`, peer ID;
- `UseShellExecute` is false, so the peer ID is never interpreted by a command shell;
- every Soltex launch receives a local confirmation and a peer-ID-free audit event.

The UI exposes explicit **not connected**, **blocked**, and **ready** states. It names RustDesk as the external owner rather than presenting a session as a native Soltex capability.

## Explicit exclusions

This slice does not:

- download or update RustDesk;
- bundle a RustDesk binary or operate its servers;
- pass a password, configure unattended access, persist credentials, or automate authentication;
- request elevation, install a service, interact with UAC, weaken Windows protections, or open firewall ports;
- hide the RustDesk interface, suppress consent, claim end-to-end security, or certify the remote endpoint;
- pin a specific RustDesk publisher certificate or prove that an arbitrary signed file named `RustDesk.exe` is an official release.

The last point is important: Authenticode trust proves that Windows trusts the current signature chain, not that Soltex has independently attested RustDesk's release provenance. A production distribution flow needs an official-release provenance policy, publisher-identity/change process, signed manifest, revocation behavior, and update rollback design.

## Threat and failure behavior

| Condition | Soltex behavior |
|---|---|
| Missing or renamed executable | Reject before creating a launch plan |
| Reparse-point executable | Reject selection or revalidation |
| File changed after approval | Block and require explicit reselection |
| Untrusted/missing Authenticode signature | Block both launch actions |
| Malformed peer ID or argument-like text | Reject before process construction |
| User cancels confirmation | Perform no launch |
| Process startup failure | Show and audit the failure; do not retry automatically |
| RustDesk transport/auth/session failure | Leave diagnosis and recovery in the visible RustDesk client |

There remains a narrow time-of-check/time-of-use interval between final byte/signature validation and Windows process creation. Same-user malware or an administrator able to replace signed executables is outside the protection of this adapter. Production hardening should use a managed installation/provenance flow rather than broadening the launcher.

## Validation state

Three deterministic current-source tests cover peer-ID injection rejection, fixed shell-free launch arguments plus fingerprint change detection, and bounded executable discovery. Later security-boundary work brings the complete current-source default suite to **27 tests** and the optional in-memory EICAR suite to **28 checks**.

The adapter, its three focused regressions within the 31-check Security suite, and the native Remote Assist render are Windows-verified at current immersive implementation commit `2a0699b2ca77b30fa636279b1d5ecab603a8bde9` in GitHub Actions run `30925606488`. The full Release build completed with zero warnings and errors; all seven focused suites passed; and all six native panels completed with no render-error files.

The optional hosted EICAR interoperability check remains 31/32 because that runner's installed AMSI provider returned native result `1`. This does not affect the RustDesk adapter boundary, but it prevents a 32/32 owner-host security-interoperability claim. Native rendering is also not owner visual acceptance, accessibility conformance, or proof of a successful RustDesk session.
