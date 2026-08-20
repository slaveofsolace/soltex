# Whisper implementation plan

Whisper is Soltex's dictation tool: hold a shortcut, speak, and cleaned text is
inserted into whatever text control currently has focus. It can optionally submit
that text, but only under a fail-closed policy.

This document is the engineering contract for the remaining work. It states what is
implemented, what is not, and the conditions each remaining piece must satisfy.

## Scope and boundaries

The target is public functional parity with the observable desktop behaviours of
commercial dictation tools — push-to-talk, hands-free, command transforms, cleanup,
snippets, personal vocabulary, per-application styles, transcript recovery, a
scratchpad, and optional submission. The implementation is independent: no third
party's source, models, prompts, assets, sounds, branding, or distinctive interface
is reproduced, and no affiliation is implied.

## Implemented and verified

The provider-neutral core in `src/Soltex.Whisper` is complete and covered by the
115-test suite in `tests/Soltex.Whisper.Tests`.

| Area | State |
|---|---|
| Session lifecycle | Idle, Listening, Transcribing, Processing, Delivering, Completed, Cancelled, Faulted, with explicit invalid-transition rejection |
| Capture modes | Push-to-talk, hands-free, command contracts; 19-minute warning and 20-minute expiry |
| Shortcuts | 4 bindings per action, 3 keys per binding, mouse buttons, order-independent conflict detection, canonical ordering |
| Text pipeline | Spoken punctuation, lines, paragraphs, bullets, parentheses, snippets, `scratch that` / `delete that` |
| Submit phrase | End-only, case-insensitive `press enter`, removed before insertion, phrase-only supported, mid-sentence treated as text |
| Delivery policy | Global consent, exact per-application profile, terminal opt-in, password/read-only/unknown/elevated fail-closed |
| Target identity | Stable runtime identity captured at session start; drift detection for process, element, editability, protection, elevation, category |
| Insertion verification | Read-back comparison tolerating only documented rich-text normalization |
| Submit gate | Origin, accepted warning, no cancellation, no drift, and verified insertion all required before Enter |
| Settings | Versioned document, per-field validation, deterministic migration, repair-to-safe defaults, reported corrections |
| Vocabulary and styles | Bounded normalized hints; message/email/document/terminal/developer styles; terminal never receives a prose style |
| Command grammar | Closed phrase table; unrecognized instructions refused; meaning-changing transforms preview before replacing a selection |
| Diagnostics | Content-free events, duration buckets, quoted-span and path redaction, bounded log |
| Readiness | One prerequisite per row with a single next action |
| Overlay presentation | Pure state machine producing every visual state including fallback and error |
| Scratchpad | Up to 5 tabs, bounded content, bounded undo depth |
| Test doubles | Deterministic capture, transcriber (including a failing one), scripted inspector, recording delivery |
| Session runner | One serialized provider-neutral path composes capture, target inspection, transcription context, finalization, delivery, and verified submission; release and cancellation are distinct, overlap is rejected, the previous completed transcript survives faults, and evidence remains content-free |
| Windows capture | Shared-mode WASAPI capture endpoints only; selected-device persistence, one default-device recovery attempt, PCM/float normalization to 16 kHz mono PCM16, a 20-minute byte bound, explicit stop versus cancellation, and throttled content-free metering |
| Capture privacy | Soltex-owned packet, conversion, accumulation, and final clip buffers are cleared on disposal; the Windows-owned WASAPI packet is released without being retained |
| Capture UI | Working device refresh, persisted selection, explicit five-second microphone test, recovery/error status, and live overlay level meter |
| Shortcut policy | Unsafe Windows-key and reserved chords are rejected with a reason; prefix-overlapping chords are rejected before registration; push-to-talk and Command Mode retain press/release semantics; hands-free double-tap becomes one lock intent rather than a second session |
| Windows shortcut adapter | Dedicated low-level keyboard/mouse hook thread, configured-key-only matching, auto-repeat suppression, Mouse 4/5 support, bounded transition queue, atomic replacement rollback, injected-input rejection, clean async disposal, and content-free callback percentiles |
| Windows target inspection | Metadata-only UI Automation adapter returns one atomic identity-and-capability snapshot; process integrity, runtime identity, editable/protected/read-only state, Value/Text/Text2 pattern availability, selection/caret support, and category are bounded behind a 750 ms single-flight timeout |
| Windows insertion adapter | Core reauthorization before mutation and again before paste; whole-value-only UIA direct set; otherwise one clipboard paste chord with a unique token, sequence ownership, bounded restore, focus-drift copy fallback, and cancellation cleanup |
| Windows verified submission | Bounded target-owned read-back, a final target reinspection, `WhisperSubmitGate` authorization, a one-use permit, and one two-event Enter dispatch; unavailable/mismatched reads, cancellation, drift, and modifier state fail closed |
| Shipped shortcut lifecycle | Persisted on/off control, registration only after settings migration/validation, visible registration state/failure, managed intent dispatch, cancellation routing, and owned shutdown drain/unregistration |
| Personalization UI | Working language and built-in style selectors plus bounded add/remove personal vocabulary, all validated through `WhisperSettingsMigrator` and persisted in the bounded per-user settings store |
| Personal library UI | Working bounded editors for snippets, named deterministic styles, and exact-process application rules; every update passes through versioned repair-to-safe settings validation, terminal eligibility depends on app auto-send eligibility, and global auto-send remains a separate consent |
| Scratchpad UI | Five session-only tabs backed by the bounded core with working edit, undo, redo, reversible clear, close confirmation for non-empty notes, and direct shortcut navigation; nothing is written to disk |
| History and privacy UI | Bounded session history has per-entry delete and clear; history-off clears it immediately; opt-in encrypted retention exposes a bounded period and clears prior authenticated generations on deletion; auto-send uses an inline first-use warning and atomic consent; context reads, clipboard behavior, and overlay preview are explicit persisted controls |
| Encrypted history retention | Up to 24 entries and 16,000 characters per entry; transcript text is protected per record with current-user DPAPI inside the existing HMAC-authenticated state envelope; load, expiry, rewrite, clear, cancellation, corruption, and no-plaintext-envelope behavior have deterministic Windows coverage |
| Provider secret boundary | Provider metadata is bounded and content-free; one provider-scoped credential is protected with current-user DPAPI inside the authenticated state envelope, acquired only through an owned zeroing lease, and rotated by removing prior Soltex generations before replacement |
| Local model ownership | `IWhisperModelManager` exposes path-free state, progress, install, repair, verification, and exact deletion. The Windows adapter pins Turbo Q5 to upstream revision `98aa99a0a9db05ae2342309f5096248665f7cba3`, exact length 574,041,195 bytes, and LFS SHA-256 `394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2`; downloads happen only after an explicit install/repair call, stream to one private bounded temporary artifact, and promote atomically after verification |
| Local provider settings | Settings version 4 carries provider, model, and runtime identity. `local-whisper` + `large-v3-turbo-q5_0` + `cpu` round-trips; the version-3 local-provider shape migrates to that tuple, while unsupported or incomplete identities repair to the disabled state |
| Local transcription adapter | `WindowsWhisperLocalTranscriber` implements the unchanged provider-neutral interface over Whisper.net 1.9.1 and its CPU runtime. It holds a verified non-delete-sharing model lease through eager native load, projects owned 16 kHz mono PCM16 through a 44-byte no-copy WAVE header, passes validated language and bounded vocabulary hints, serializes one lazy native runtime, bounds output, supports cancellation/unload, clears owned capture buffers through the existing clip lifetime, and emits content-free failure categories only |
| Shipped local setup | The WPF Setup surface exposes explicit local-provider selection, install, repair, cancellation, verified status, progress, and confirmed exact-owned deletion. No model download starts at launch or merely by opening Whisper; model repair/deletion first cancels and drains dictation and unloads the native runtime |
| Shipped session composition | The app instantiates the production WASAPI capture, local transcriber, UI Automation inspector, insertion adapter, verified submitter, and provider-neutral session runner. Registered begin/release/toggle/cancel intents use this one owned path; saved exact-process profiles are resolved after target inspection; 19-minute warning/20-minute expiry, overlay state, optional bounded history, last-transcript actions, model mutation, and shutdown all share explicit owned task lifetimes |
| Packaged CPU runtime | The `win-x64` package ships exactly the four Whisper.net CPU native DLLs beside the single-file app in the runtime layout the loader probes. A content-free packaged-process gate loads that runtime while proving no model or microphone was opened; the package is bounded below 256 MiB and rejects the 547 MiB model filename. This proves runtime availability, not transcription accuracy or model performance |
| Whisper UI evidence | The shipped manifest declares per-monitor-v2 awareness. A bounded direct-process renderer captures light, dark, high-contrast, minimum-width, and 100/150/200-percent raster profiles plus every presenter-owned overlay state; each artifact records the exact owned PID, dimensions, evidence class, commit identities, and SHA-256 without microphone, transcript, clipboard, target, or model content |

The WPF surfaces in `src/Soltex.App` — navigation entry, Whisper page with Setup,
Shortcuts, Personalize, Library, Scratchpad, History, and Privacy tabs, and the floating listening
surface — render this core.
They are honest about capability: the navigation entry remains marked `SCAFFOLD`
because the production model has not been downloaded or exercised on the owner-controlled host and
the complete application matrix is still pending. The shipped session and model controls are real,
but readiness stays blocked until Whisper is enabled, one current input exists, shortcuts are
registered, the exact local provider tuple is selected, and the pinned model verifies. Capture
controls are enabled only when a selected device exists. Auto-send stays off by default, and no host
code authorizes Enter outside the provider-neutral submit gate.

## Not implemented

Nothing below exists yet. Any claim that it works is false until runtime evidence
from an owner-controlled Windows host says otherwise.

1. Owner-controlled download and execution of the pinned 547 MiB production model, including
   accuracy, latency, working-set, cancellation, unload, and restart evidence.
2. The complete owner-controlled target/insertion/submission matrix for Win32, WPF, WinUI,
   Chromium, Electron, Windows Terminal, read-only, elevated, unknown, and focus-changing targets.
   The shipped adapters are wired, but the full matrix is not proven.
3. Physical keyboard and mouse shortcut coverage plus callback and shortcut-to-listening latency
   measurements in the packaged app.
4. Owner-controlled per-monitor DPI transitions, keyboard-only and screen-reader walkthroughs,
   performance, install, update, and uninstall evidence for the Whisper surfaces. The
   deterministic renderer now covers light/dark/high-contrast, minimum-width, synthetic
   100/150/200-percent raster profiles, and every overlay state. Synthetic high-density
   output is not a claim that live Windows DPI transitions passed. The self-contained package
   proves its CPU native runtime and model exclusion, but install/update/uninstall remain pending.
5. Owner-controlled unplug/reconnect proof across a representative microphone matrix;
   deterministic tests currently prove the recovery policy and one owner-host device
   proves the normal live path.

## Remaining work

### 1. Capture

Implemented in `src/Soltex.Whisper.Windows` without an external audio dependency.
`WhisperWasapiCaptureSource` uses shared-mode WASAPI capture endpoints only, persists
the selected endpoint through the versioned Whisper settings document, normalizes
supported PCM and 32-bit float input to 16 kHz mono PCM16, and makes shortcut release
an explicit successful stop while cancellation remains the discard path. Capture is
bounded to 38,400,000 bytes (20 minutes), and one invalidated selected device can
recover to the current Windows default during the same session.

Level metering is computed from the packet being appended, emitted no more often than
roughly 20 Hz, and retains no additional audio. Permission denied, no device,
exclusive use, disconnect, unsupported format, no-audio, and unavailable failures are
stable categories mapped into readiness. Soltex-owned raw copies, normalized packets,
pooled accumulation chunks, and final clips are explicitly cleared on every disposal
path. WASAPI's engine-owned buffer is released through `IAudioCaptureClient` and is
never retained or modified.

Whisper reuses the existing `Soltex.Audio` MMDevice declarations instead of loading a
second managed identity for the same Windows COM interfaces. The shell also completes
its initial audio inventory before starting Whisper device discovery. This prevents a
cold-start RCW identity collision while preserving later user-requested refreshes.

Deterministic tests cover selected-device fallback, disconnect recovery, cancellation
during enumeration/open/read, the byte bound, float-stereo normalization, readiness
mapping, metering, and buffer clearing. An owner-host run observed 5 active capture
devices and captured 31,040 PCM bytes over 970 ms from the selected default device
without fallback; the clip was then disposed. A separate cold-start app run exposed
all 5 devices without a manual retry. That proves one normal live path only.
It does not prove every microphone driver, a physical unplug/reconnect, or Windows
privacy-denial recovery.

### 2. Transcription provider

The first provider is fixed to fully local transcription: provider
`local-whisper`, multilingual model `large-v3-turbo-q5_0`, runtime `cpu`. The model
is not in the installer. `WindowsWhisperLocalModelManager` creates only
`whisper\models` beneath the canonical Soltex data root, never exposes that path to
UI policy, and performs network access only from an explicit `InstallAsync` or
`RepairAsync` call. It accepts one exact HTTPS artifact pinned to upstream revision
`98aa99a0a9db05ae2342309f5096248665f7cba3`, 574,041,195 bytes, and LFS SHA-256
`394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2`.

The response must be an unencoded 200 response with the exact declared length. The
manager reads at most the expected length plus one byte, hashes while writing to one
unique private partial file, flushes it durably, closes it, atomically moves it into
place, then reopens the destination and compares Windows file identity with the
identity captured from the owned write handle. One in-process gate and one
cross-process exclusive lock reject concurrent writers. Recognized interrupted
partial names are removed only during another explicit install/repair; deletion
removes the exact model and recognized owned partials, preserving unrelated state.

The pinned digest is an integrity expectation derived from the upstream LFS object
at that revision. It is not described as independent publisher authenticity proof.
The 59-case Windows adapter suite covers normal install, declared and streamed
oversize, truncation, digest mismatch, cancellation cleanup, concurrent rejection,
repair, exact deletion, interrupted partial cleanup, and post-install change
detection with small benign fixtures. The 547 MiB production model was not
downloaded in this stage, and no latency, memory, transcription-quality, or provider
readiness claim is made.

The provider-neutral `WhisperProviderStatus` contract now exposes bounded
endpoint/model identity, language capability, streaming capability, a privacy
statement, and credential availability without carrying a credential. The Windows
`WindowsWhisperCredentialStore` adapter uses a provider-scoped
`AuthenticatedProtectedSecretStore`: current-user DPAPI protects the clear bytes,
the existing authenticated envelope protects on-disk integrity, and callers receive
an owned `WhisperCredentialLease` whose bytes are cleared on disposal. Rotation
removes the prior current and backup generations before committing the replacement;
deletion removes only the provider's exact state artifacts and preserves the shared
authenticated-state key. Bounds, round-trip, no-plaintext state, corruption,
cancellation, rotation, deletion, and lease clearing have deterministic coverage.

`WindowsWhisperLocalTranscriber` now implements `IWhisperTranscriber` using
Whisper.net 1.9.1 with `Whisper.net.Runtime` forced to the CPU backend. The adapter
rejects non-16 kHz mono PCM16 and fewer than 201 samples before native entry, then
exposes the existing owned capture memory through a read-only RIFF/WAVE stream with
only a 44-byte header allocation. It never clones the full audio buffer. Language is
validated and reduced from an owner setting such as `en-US` to the provider language
identifier; personal vocabulary is passed as a bounded initial prompt and is never
used to rewrite recognized words.

Model verification and eager native loading overlap under one non-delete-sharing
read lease. Native access is serialized, one runtime is loaded lazily, and a
processor is reused only while its language/prompt configuration matches. The
Whisper.net optional managed string pool is explicitly disabled. Empty or oversized
output fails, runtime faults unload the failed native runtime before a later retry,
explicit unload permits model repair/deletion, and cancellation after runtime
creation disposes the newly created runtime before returning. Diagnostics contain
only a duration bucket, fixed provider/model/runtime identities, result category, and
fixed sanitized failure category.

Seven new benign adapter tests prove the model lease, no-copy WAVE projection,
language and vocabulary propagation, lazy reuse, runtime-fault recovery, content-free
failures, cancellation classification, short/empty/oversized rejection, explicit
unload/reload, and owned PCM clearing. Together with the existing model-manager and
Windows coverage, the suite reports 59/59. The production model was not downloaded
or executed: there is no live accuracy, latency, working-set, readiness, or supported-
hardware claim. The shipped Setup surface and session composition now use this adapter;
owner-controlled live proof remains required.
This local provider has no credential or cloud endpoint and uploads no audio or
transcript.

A provider returns transcription and optional polish metadata only. It never issues
clicks, keypresses, application commands, or submission decisions. The deterministic
policy remains the sole actuator.

### 3. Global shortcuts

The provider-neutral policy and Windows adapter are implemented. The low-level hook
runs on a dedicated message thread, retains only state for keys present in the
validated shortcut set, ignores injected input, queues content-free action
transitions to one managed reader, and never suppresses system input. Chords match in
any key order, key auto-repeat cannot start a second action, and left/right modifier
state is reference-counted. Mouse 4 and Mouse 5 use the same transition model.

Registration builds a disabled candidate first. Only a successfully installed
keyboard/mouse pair replaces the active registration; a partial or failed replacement
leaves the previous valid set running. Disposal disables callbacks before posting the
hook thread's quit message and awaiting the dispatch worker. Platform-neutral gesture
policy maps push-to-talk and Command Mode to press/release intents and maps a hands-free
double-tap to one lock intent rather than a second session.

The owner-host native lifecycle proof installed and removed the production hooks and
fed one six-event injected sequence. Production correctly dispatched zero actions
from that sequence while still recording content-free Soltex callback work before
control passes to the next hook. That proves
installation, callback entry, injected-input rejection, measurement, and teardown; it
does not prove a physical keyboard/mouse matrix or a provider-backed shipped session.

The shipped WPF page now owns a persisted runtime toggle. Enabling it after
`WhisperSettingsMigrator` validation registers the default set; disabling it or
closing Soltex drains the owned lifecycle task and disposes the hook host. Readiness
uses the observed registration state and sanitized failure. Begin/release, Command Mode,
hands-free toggle/lock, Escape cancellation, Scratchpad, and paste/copy/submit-last intents
now route through one deterministic host router into the shipped session or last-transcript path.
Render-evidence mode never installs global hooks.

`WhisperSessionRunner` now supplies the tested provider-neutral coordinator path. It
starts target inspection alongside capture, passes vocabulary/language/style hints to
the transcriber, rejects overlapping sessions, separates successful release from
discarding cancellation, preserves the previous completed transcript after a fault,
and delegates all delivery/submission authorization to the existing core policies.
Its structured lifecycle evidence contains only categories and fixed reasons.

The app now instantiates that runner with the selected local provider and all Windows adapters.
It resolves an exact saved application profile only after inspection and keeps delivery and Enter
authorization in the existing provider-neutral policies. Still required: prove physical
push-to-talk release and mouse buttons in the shipped app and record shortcut-to-listening latency.
Current owner-host proof covers the adapter lifecycle; the WPF lifecycle path has deterministic
UI/build/render coverage but not a production-model packaged-app run.

### 4. Target inspection

Implemented in `src/Soltex.Whisper.Windows`. `IWhisperTargetInspector` now returns a
nullable `WhisperTargetSnapshot` so identity and capabilities come from one atomic
inspection rather than from independently timed reads. `WindowsWhisperTargetInspector`
runs UI Automation provider work on one background MTA thread and applies one total
750 ms deadline. If a provider is slow, throws, changes focus during inspection, or
cannot expose a stable identity and integrity level, the result is unknown. Only one
native inspection can remain in flight, which prevents a non-responsive provider
from creating an unbounded thread or queue.

The native adapter reads process ID/name, bounded content-free framework ID, token integrity level,
opaque runtime ID,
enabled/focusable/password state, control type, and advertised Value, Text, and
TextPattern2 support. It queries Value/Text read-only and selection metadata only for
non-password controls. It never requests UIA Name, a Value value, text-range text,
selected text, captions, or surrounding text. Password providers are not opened.
`WhisperTargetCapabilities` carries only boolean pattern metadata; browser, editor,
terminal, plain-text, and rich-text categories remain content-free. Soltex stays
`asInvoker` with `uiAccess=false`; high/system/protected targets are identified and
the existing delivery policy falls back to copy.

Target-specific deterministic coverage is included in the current 59-case Windows adapter suite.
It covers all five target categories, pattern capability mapping,
protected/read-only/unknown controls, provider timeout, provider failure, and
cancellation. An opt-in owner-host run at commit `d11edec` also inspected a real
focused rich-text control in 72.04 ms and reported zero content reads. This is
adapter proof, not the full Win32/WPF/WinUI/Chromium/Electron/Terminal/elevated
matrix. The adapter is now part of the shipped session. An additional opt-in owner-host matrix creates
controlled native WPF targets and drives the production inspector against WPF
TextBox, RichTextBox, PasswordBox, read-only TextBox, and a focus-changing TextBox.
The same run proves direct whole-value replacement for TextBox, clipboard paste with
owned restoration for RichTextBox, and core copy-only authorization for protected,
read-only, and drifted snapshots. It emits categories and outcomes only. WinForms
TextBox continues to provide the controlled Win32 Edit proof. Separate controlled Chromium input and
contenteditable targets now prove browser classification, clipboard insertion, target-owned
read-back, and one authorized Enter per target; the isolated browser process tree and profile are
exact-owned and removed after the run.
Non-browser Chromium-framework providers can now classify as `Browser`, while known editor processes
retain `Editor` precedence. WinUI, a distinct Electron application, Windows Terminal, and an elevated
editor remain unproven. The exact support/proof split is maintained in
`docs/WHISPER_TARGET_MATRIX.md`. Context reads
remain off; a future visible per-application permission must precede any bounded
contextual read.

### 5. Insertion

Implemented in the provider-neutral core and `src/Soltex.Whisper.Windows` and now
wired into the shipped session. `WhisperInsertionPolicy` reauthorizes the core's
delivery decision against the captured and current target before Windows code may
act. Unknown, read-only, protected, stale, focus-changing, and cross-integrity targets
reduce to a clipboard copy with a fixed content-free reason; a prior copy decision
can never be upgraded to insertion.

`WindowsWhisperTextDelivery` inspects again before mutation and once more after
staging the clipboard. A direct `ValuePattern.SetValue` is attempted only when the
same focused control exposes both Value and Text patterns, is enabled, focusable,
non-password, writable, and its one selection covers the entire document. That
preserves whole-value replacement semantics without reading the value or text. Every
other editable case uses a single four-event `Ctrl+V` sequence; it never types the
transcript character by character. Existing modifier state causes paste to fail
closed, and Windows UIPI remains authoritative.

Clipboard text is bounded to the transcript limit and tagged with a unique
operation token. The Windows clipboard sequence number is captured only after both
the text and token are rendered. Optional restore retains the previous OLE data
object without inspecting its formats, then restores it only if both the sequence
and token still match. If another owner changes the clipboard, restore is skipped.
Cancellation before paste restores an owned clipboard when requested. If focus drifts
after staging or paste input is rejected, the transcript remains copied and the
result names the fallback. Clipboard acquisition and restoration use bounded retries
on one background STA thread.

The core suite has 115 cases and the Windows suite has 59 deterministic cases. The
Windows cases cover direct-before-clipboard ordering, ownership restoration and loss,
focus drift after staging, unknown targets, rejected paste, and cancellation cleanup.
An opt-in owner-host test uses a controlled WinForms text target to prove clipboard
paste, ownership-checked prior-content restoration, and whole-value UI Automation
replacement. Exact current timings are retained in the commit-matched live evidence.
The harness reads the controlled target to prove the mutations; the adapter result
itself is intentionally named
`MutationDispatched`, not `Inserted`, because `SendInput` success is not insertion
proof.

### 6. Verified submission

Implemented in the provider-neutral core and `src/Soltex.Whisper.Windows` and now
wired into the shipped session. After a dispatched insertion,
`WindowsWhisperVerifiedSubmitter` waits one bounded 75 ms verification window,
re-inspects the target, and asks `WindowsWhisperTargetTextReader` for at most
400,000 characters of the focused control's own Value or TextPattern state. The
ephemeral read-back is never logged or persisted and is passed directly through
`WhisperInsertionVerifier`.

The target is inspected once more immediately before submission. Every decision then
passes through `WhisperSubmitGate`: requested origin, accepted first-use warning,
cancellation, target drift, and verified insertion must all remain valid. An allowed
authorization carries a one-use permit. The Windows adapter consumes it immediately
before one Enter-down/Enter-up `SendInput` call; a denied or already-consumed
authorization cannot emit input. Active Ctrl, Shift, Alt, or Windows modifiers reject
the dispatch after consuming the permit, so no retry can accidentally submit later.

The Windows suite exercises every gate branch end-to-end, altered and unavailable
read-back, focus change between verification and Enter, pre-irreversible
cancellation, submit-only behavior, rejected dispatch, provider timeout/failure,
bounded reads, and one-use authorization. A successful synthetic-input call remains
dispatch evidence only. The controlled live harness must separately observe exactly
one Enter on the same verified control before this slice is claimed at a commit.

Whisper never selects a recipient, channel, conversation, address, or terminal. A
named destination shortcut may focus an owner-configured application, but still
requires a confirmed editable target.

### 7. Remaining UI

Local-provider selection and model install/repair/cancel/delete/status are implemented. Session
history with per-entry deletion and clear, the first-use auto-send warning, snippet, vocabulary,
custom-style, per-application, Scratchpad, language, privacy, and device controls are implemented.
Every enabled control must execute. Unavailable
capability is an explicit state, not a disabled control that looks operable.

At narrow window widths all core controls stay reachable without horizontal
scrolling. The fixed navigation rail scrolls vertically at the supported minimum
height so lower routes are not silently clipped. The application manifest declares
`PerMonitorV2,PerMonitor` with the legacy `true/pm` fallback.

`eng/capture-whisper-ui-evidence.ps1` directly launches the built app without a shell,
uses one bounded process per state, records the exact owned PID, rejects stale artifact
reuse, and captures 22 content-free states: the Whisper Setup page in light, dark, and
high contrast; standard and minimum-size 100-percent profiles; deterministic 150- and
200-percent high-density profiles; all 13 visible presenter states; and representative
overlay theme/density variants. The manifest records logical and pixel dimensions,
evidence class, commit identities, and SHA-256. The 150/200-percent profiles prove
high-density rasterization only; an owner-controlled live per-monitor transition remains
required. Keyboard-only operation and screen-reader labelling remain owner-proof gates,
not inferred completion from render output.

### 8. Retention

Optional disk retention is implemented and stays off by default. The provider-neutral
`IWhisperHistoryRetentionStore` is implemented on Windows by
`WindowsWhisperHistoryRetentionStore`; it maps only fixed delivery categories and
bounded metadata into `AuthenticatedProtectedHistoryStore`. Transcript text is
UTF-8 encoded, protected per record with current-user DPAPI, and then stored inside
the existing generation-numbered, HMAC-authenticated Soltex state envelope. The
shared authenticated-state key remains DPAPI-protected and is not duplicated for
Whisper.

Retention is limited to 1–90 days, 24 disk entries, and 16,000 characters per disk
entry. Session memory remains independently bounded to 100 entries. Startup rejects
unsupported versions, entry counts, metadata, ciphertext sizes, authentication
failures, DPAPI failures, and invalid UTF-8 rather than exposing partial history.
Expiry and per-entry deletion use a privacy-prioritized rewrite: current and backup
Whisper generations are removed before the surviving set is committed. Clear removes
both exact Whisper history artifacts immediately while preserving the shared state
key used by other Soltex stores. A failed transition into encrypted mode attempts to
remove staged history before reporting an error.

The Privacy page exposes Off, This session only, and Encrypted on this PC, with an
explicit retention selector. Leaving encrypted mode deletes Soltex-owned retained
history before the new setting is accepted. History text is never copied into the
global Activity feed or diagnostic events.

The 18-case security-hardening suite covers authenticated round trip, absence of
plaintext in either envelope, expiry, current/backup deletion, bounds, cancellation,
and corruption failure. The 59-case Windows adapter suite covers the Whisper mapping
and rewrite boundary. This is application-level encrypted retention, not forensic
secure erasure: filesystem snapshots, SSD remapping, page files, crash dumps, and a
same-user process able to invoke DPAPI remain outside its guarantee.

The owner-host WPF matrix is intentionally excluded from hosted CI because an
interactive desktop and real UI Automation providers are required. Run it explicitly
with `SOLTEX_RUN_WHISPER_TARGET_MATRIX=1`; the current local proof reports 41/41,
including PlainText/AutomationValue, RichText/ClipboardPaste, protected-field,
read-only, and focus-drift outcomes with `content_logged=0`.

### 9. Threat model

Extend `docs/THREAT_MODEL.md` for global-hook abuse and accidental keylogging,
microphone consent and stale capture, provider credential and data exposure,
context injection, protected fields, clipboard races and exfiltration, focus
time-of-check/time-of-use changes, integrity-boundary and elevated applications,
terminal command execution, accidental submission, retention on multi-user machines,
malicious or inaccessible automation providers, and denial of service from long
audio or oversized text.

### 10. Packaging checkpoint

`Soltex.Whisper.Windows` suppresses the dependency package's all-architecture build
assets and explicitly publishes only `whisper.dll`, `ggml-whisper.dll`,
`ggml-base-whisper.dll`, and `ggml-cpu-whisper.dll` under
`runtimes\win-x64`. They remain loose beside the single-file executable because the
Whisper.net loader probes that runtime-specific directory. The package gate requires
that exact set, rejects the pinned model filename, bounds the complete package below
256 MiB, and launches `Soltex.exe --whisper-runtime-probe` in a separate process.

The probe records only fixed provider/runtime identity, commit identity, availability,
and explicit `modelOpened=false` / `microphoneOpened=false` fields. It does not create a
window, instantiate the normal app runtime, read the model store, enumerate capture
devices, or report native feature strings. A passing probe proves that the shipped CPU
dependency chain loads. It does not prove the production model, transcription,
accuracy, latency, memory use, microphone permission, or insertion path.

The per-user Inno installer copies the same exact four CPU runtime DLLs into the
runtime-specific directory beside the installed single-file app. Its ephemeral-host
gate installs version 0.0.1, verifies that exact runtime payload, stages bounded
Whisper ownership fixtures, upgrades to 0.0.2 while proving those fixtures remain,
then performs a silent uninstall. Before installed files are removed, the app's exact
controlled cleanup removes the pinned model artifact, recognized interrupted model
and settings files, encrypted Whisper history, and the local-provider credential
generations. It preserves unrelated model-directory files, the shared authenticated
state key, and all non-Whisper Soltex state. The cleanup is separately covered by a
benign filesystem test and emits only a content-free result when evidence is requested.

## Evidence required before Whisper is called complete

- Release build with zero warnings and zero errors.
- Core, settings, provider, integration, accessibility, and package tests passing.
- Every shipped control demonstrably executing.
- Insertion proven across Win32 Edit, WPF text controls, WinUI, Chromium
  contenteditable and input, Electron, a terminal, an elevated editor, a read-only
  field, and a target that changes focus mid-insertion.
- Auto-send off by default, with Enter never emitted without full authorization and
  verified insertion.
- Password, read-only, unknown, stale, elevated, and terminal cases failing closed.
- No transcript or audio content in any log or artifact.
- Light, dark, high-contrast, narrow-width, keyboard, and screen-reader checks.
- Package, install, update, and uninstall evidence including the Whisper assembly.
- Measured performance: idle working set and CPU, hook callback p50/p95/p99,
  capture buffer bounds, shortcut-to-listening latency, release-to-first-request
  latency, insertion and verified-submit latency, disposal after cancellation and
  shutdown.

Latency targets are not claimed as met until measured on an owner-controlled
Windows host. Hosted CI cannot substitute for automation, microphone, elevation, or
live provider proof; that limitation is classified honestly rather than mocked away.
