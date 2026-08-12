# Audio session controls

Snapshot: 2026-08-11
Status: owner-host exact-source verified at `3511ba92bde450ffac4e3fad145d9a13b8986e72`; hosted exact-head verification pending

## User capability

The Audio workspace observes Windows Core Audio endpoints and active shared-mode playback sessions. App sessions are the primary surface. Device inventory is summarized first and expands only when the user requests it.

For an eligible app session, Soltex exposes two direct controls:

- session master volume from 0 through 100 percent;
- session mute/unmute.

Soltex reports success only after Windows returns the requested state through an immediate `ISimpleAudioVolume` read-back. A rejected, ended, changed, inaccessible, or mismatched session remains visibly non-successful.

## Observation bounds and privacy

- At most 32 active render endpoints are inspected for sessions.
- At most 128 session slots are inspected and at most 24 active sessions are exposed.
- Only active shared-mode render sessions are shown. Inactive and expired sessions are not retained as history.
- Display names are control-character stripped, whitespace-normalized, path-like values rejected, and limited to 80 characters.
- Soltex never requests or displays executable paths, command lines, session icon paths, or raw session identifiers.
- Raw endpoint and session-instance identifiers are reduced to SHA-256 identities held only in the current process. They are not exposed, persisted, or written to Activity.
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

The controlled live-write check is opt-in through `SOLTEX_RUN_AUDIO_WRITE_TEST=1`; the default automated suite never changes a live app's audio. Timing is diagnostic for this host, not a performance guarantee. Full paths, hashes, and the bookkeeping-retry record are in [`evidence/2026-08-11-product-rebuild/AUDIO_SESSION_GATE.md`](evidence/2026-08-11-product-rebuild/AUDIO_SESSION_GATE.md). Hosted exact-head and package evidence remain required before this slice is accepted for merge.

## Nonclaims

This slice does not implement endpoint switching, per-app routing, loopback, audio capture, virtual endpoints, EQ, DSP, compression, spatial processing, noise suppression, microphone processing, profiles, or a SteelSeries Sonar replacement. Stable routing or processing would require a separately designed, signed, installed, and independently tested Windows audio component.

## Primary Windows references

- [`IAudioSessionManager2::GetSessionEnumerator`](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessionmanager2-getsessionenumerator)
- [`IAudioSessionEnumerator`](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nn-audiopolicy-iaudiosessionenumerator)
- [`IAudioSessionControl2::GetProcessId`](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessioncontrol2-getprocessid)
- [`ISimpleAudioVolume`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nn-audioclient-isimpleaudiovolume)
- [`ISimpleAudioVolume::SetMasterVolume`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-isimpleaudiovolume-setmastervolume)
