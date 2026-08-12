# Audio session controls and device handoff

Snapshot: 2026-08-12
Status: session controls hosted-accepted on PR head `abf1dc5e58ca7a23ef57a975c7fdeb041ea4d183`; fallback/device handoff owner-host accepted at source `1d15071a14472dce199796a57966bddbf7be2fc4`; new hosted gate pending

Current branch: local fallback reminders plus a user-mediated Windows Sound settings handoff are implemented and owner-host verified; publication reconciliation remains pending

## User capability

The Audio workspace observes Windows Core Audio endpoints and active shared-mode playback sessions. App sessions are the primary surface. Device inventory is summarized first and expands only when the user requests it.

For an eligible app session, Soltex exposes two direct controls:

- session master volume from 0 through 100 percent;
- session mute/unmute.

Soltex reports success only after Windows returns the requested state through an immediate `ISimpleAudioVolume` read-back. A rejected, ended, changed, inaccessible, or mismatched session remains visibly non-successful.

Device management stays behind **Manage devices**. An active playback or recording endpoint can be remembered as a fallback reminder. This does not switch the Windows default. Soltex exposes one fixed **Windows Sound** action so the user can make the system-owned choice in Settings and then refresh the observation.

## Observation bounds and privacy

- At most 32 active render endpoints are inspected for sessions.
- At most 128 session slots are inspected and at most 24 active sessions are exposed.
- Only active shared-mode render sessions are shown. Inactive and expired sessions are not retained as history.
- Display names are control-character stripped, whitespace-normalized, path-like values rejected, and limited to 80 characters.
- Soltex never requests or displays executable paths, command lines, session icon paths, or raw session identifiers.
- Raw endpoint and session-instance identifiers are never exposed or written to Activity. Session identities remain process-only. When the user explicitly saves a fallback reminder, Soltex persists only a direction-scoped, lowercase SHA-256 endpoint fingerprint in the bounded local preference document; the raw Windows endpoint ID and friendly name are not persisted there.
- System-sounds, multi-process/transferred, ended, and process-unverifiable sessions remain read-only.

## Write admission and recovery

Every explicit write re-enumerates current active render endpoints and revalidates:

1. the one-way endpoint identity;
2. the one-way session-instance identity;
3. active session state;
4. non-system-sounds status;
5. owning process ID;
6. owning process start time.

If any identity changed, Soltex returns `TargetChanged` without writing. A supported write uses a unique Core Audio event-context GUID and immediately reads both master volume and mute state. The UI refreshes from Windows after the request and records only the sanitized result as a meaningful Audio event.

## Default and fallback boundary

`IMMDeviceEnumerator::GetDefaultAudioEndpoint` observes the current default for the requested direction and role; it does not assign that role. Microsoft documents that an application cannot change the system-assigned device role. Soltex therefore does not bind the undocumented `IPolicyConfig` interfaces used by some third-party utilities.

The supported workflow is deliberately user-mediated:

1. Soltex shows the current Windows-reported defaults.
2. The user may save one playback and one recording fallback reminder from active endpoints.
3. Preference schema 3 stores only the two normalized 64-character fingerprints and recovers invalid values to empty.
4. **Windows Sound** launches the fixed `ms-settings:sound` URI with no user-controlled command, argument, or executable input.
5. Windows owns the actual default-device choice; the user returns to Soltex and refreshes.

A reminder reports only whether the saved endpoint is currently active. It does not perform failover, background switching, routing, or recovery automation.

## Evidence

Owner-host gates on exact source `3511ba92bde450ffac4e3fad145d9a13b8986e72`:

- Release `Soltex.Audio` and `Soltex.App` builds: 0 warnings, 0 errors;
- standard Audio suite: 20/20;
- opt-in controlled live-write Audio suite: 21/21;
- App/control suite: 19/19;
- full Release solution, identity, design, Security/EICAR, supply-chain, hardening, Updates, Device Fabric, and Monitoring gates: passed;
- live read-only session capture: 5 active sessions exposed from 28 observed slots, 0 inaccessible, 0 omitted, about 8 ms provider time on this host;
- controlled write: a task-owned silent WinMM loop created a session for the test process, wrote its already-observed volume and mute values back unchanged, confirmed both through Core Audio, stopped playback, and removed the temporary fixture;
- native 1280x820 default and explicitly expanded-device Mixer captures: generated and directly inspected.
- self-contained `win-x64` package: 71,559,764 bytes, exact SHA-256 recorded, native launch/render passed, truthfully `NotSigned`.

The controlled live-write check is opt-in through `SOLTEX_RUN_AUDIO_WRITE_TEST=1`; the default automated suite never changes a live app's audio. Timing is diagnostic for this host, not a performance guarantee. Full paths, hashes, and the bookkeeping-retry record are in [`evidence/2026-08-11-product-rebuild/AUDIO_SESSION_GATE.md`](evidence/2026-08-11-product-rebuild/AUDIO_SESSION_GATE.md).

Hosted acceptance on published PR head `abf1dc5e58ca7a23ef57a975c7fdeb041ea4d183` passed Windows run `31562540695` and package-smoke run `31562540694`. Downloaded artifacts `9128308988` and `9128281441` independently matched GitHub digests `sha256:dd0c089596cbbdd09f79140bf9251770dcf18e482ec46bbef3ccd5aa6537e386` and `sha256:ff0df6759cd4b9ff1c8387d174c74d347f5774b1bb446f9e8e4a11c0d68f12c6`. All 14 manifest-bound 1280x820 PNGs revalidated byte-for-byte; default and expanded Audio plus packaged Home were directly inspected. The packaged executable was 71,581,365 bytes, SHA-256 `898fd206951392c837c37dcd0b41178320ab1fd23cc7376819a3f4fb920132a3`, launched with exit code 0, and remained truthfully unsigned. The optional hosted AMSI/EICAR provider check remained 31/32 with native result `1`; required gates passed and this does not support a detection-efficacy claim.

The fallback/device-handoff production source `1d15071a14472dce199796a57966bddbf7be2fc4` passed owner-host Release build, identity/design policies, Security/EICAR 32/32, Monitoring 16/16, Audio 21/21, App/control 29/29 on the strengthened descendant test head, a 16/16 native matrix, runtime lifecycle probe, and self-contained package renders. The first exact matrix attempt correctly failed on a disposed telemetry cancellation source; the accepted source serializes start/stop/disposal under one owner and did not reproduce the race across the full matrix. Default, disclosed-device, full-inventory, and packaged-device pixels were directly inspected. Exact results, hashes, failure/recovery evidence, and nonclaims are in [`evidence/2026-08-12-audio-fallback`](evidence/2026-08-12-audio-fallback/README.md). Hosted acceptance of the new published head remains pending.

## Nonclaims

This slice does not assign Windows default endpoints, perform automatic failover, implement per-app routing, loopback, audio capture, virtual endpoints, EQ, DSP, compression, spatial processing, noise suppression, microphone processing, profiles, or a SteelSeries Sonar replacement. Stable routing or processing would require a separately designed, signed, installed, and independently tested Windows audio component.

## Primary Windows references

- [`IAudioSessionManager2::GetSessionEnumerator`](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessionmanager2-getsessionenumerator)
- [`IAudioSessionEnumerator`](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nn-audiopolicy-iaudiosessionenumerator)
- [`IAudioSessionControl2::GetProcessId`](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessioncontrol2-getprocessid)
- [`ISimpleAudioVolume`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nn-audioclient-isimpleaudiovolume)
- [`ISimpleAudioVolume::SetMasterVolume`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-isimpleaudiovolume-setmastervolume)
- [`IMMDeviceEnumerator::GetDefaultAudioEndpoint`](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immdeviceenumerator-getdefaultaudioendpoint)
- [Using a Communication Device](https://learn.microsoft.com/en-us/windows/win32/coreaudio/using-the-communication-device)
- [Launch Windows Settings](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings)
