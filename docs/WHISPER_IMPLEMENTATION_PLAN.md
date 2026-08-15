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
97-test suite in `tests/Soltex.Whisper.Tests`.

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
| Windows capture | Shared-mode WASAPI capture endpoints only; selected-device persistence, one default-device recovery attempt, PCM/float normalization to 16 kHz mono PCM16, a 20-minute byte bound, explicit stop versus cancellation, and throttled content-free metering |
| Capture privacy | Soltex-owned packet, conversion, accumulation, and final clip buffers are cleared on disposal; the Windows-owned WASAPI packet is released without being retained |
| Capture UI | Working device refresh, persisted selection, explicit five-second microphone test, recovery/error status, and live overlay level meter |
| Shortcut policy | Unsafe Windows-key and reserved chords are rejected with a reason; prefix-overlapping chords are rejected before registration; push-to-talk and Command Mode retain press/release semantics; hands-free double-tap becomes one lock intent rather than a second session |
| Windows shortcut adapter | Dedicated low-level keyboard/mouse hook thread, configured-key-only matching, auto-repeat suppression, Mouse 4/5 support, bounded transition queue, atomic replacement rollback, injected-input rejection, clean async disposal, and content-free callback percentiles |
| Windows target inspection | Metadata-only UI Automation adapter returns one atomic identity-and-capability snapshot; process integrity, runtime identity, editable/protected/read-only state, Value/Text/Text2 pattern availability, selection/caret support, and category are bounded behind a 750 ms single-flight timeout |
| Windows insertion adapter | Core reauthorization before mutation and again before paste; whole-value-only UIA direct set; otherwise one clipboard paste chord with a unique token, sequence ownership, bounded restore, focus-drift copy fallback, and cancellation cleanup |
| Windows verified submission | Bounded target-owned read-back, a final target reinspection, `WhisperSubmitGate` authorization, a one-use permit, and one two-event Enter dispatch; unavailable/mismatched reads, cancellation, drift, and modifier state fail closed |

The WPF surfaces in `src/Soltex.App` — navigation entry, Whisper page with Setup,
Shortcuts, and Privacy tabs, and the floating listening surface — render this core.
They are honest about capability: the navigation entry remains marked `SCAFFOLD`
while providers and shipped shortcut/target/insertion/submission session wiring are
unavailable. The Windows adapters exist but are not yet connected to a shipped
session, so the page does not claim it is ready. The capture controls are enabled
only when a selected device exists.

## Not implemented

Nothing below exists yet. Any claim that it works is false until runtime evidence
from an owner-controlled Windows host says otherwise.

1. A real transcription provider adapter and its credential storage.
2. Shipped shortcut registration and dispatch into the real session coordinator.
3. Shipped session wiring for the UI Automation inspector, plus the complete
   owner-controlled target matrix (the adapter and one live focused-control proof
   exist).
4. Shipped session wiring and the complete application matrix for the insertion
   adapter (one controlled WinForms target has live direct and clipboard proof).
5. Shipped verified-submission wiring and the complete application/denial matrix
   (the adapter and one controlled target proof exist).
6. Scratchpad UI, snippet/vocabulary/style editors, and history UI.
7. Encrypted transcript retention.
8. Full accessibility, scale, theme, performance, install, update, and uninstall
   evidence for the Whisper surfaces.
9. Owner-controlled unplug/reconnect proof across a representative microphone matrix;
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

Implement `IWhisperTranscriber` for at least one real provider alongside the
existing deterministic double. Provider settings expose endpoint/model identity,
language capability, streaming capability, a privacy statement, and credential
availability. Credentials use the repository's secret-storage boundary and never
appear in source files, plain JSON, process arguments, or logs.

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
does not prove a physical keyboard/mouse matrix or shipped session dispatch.

Still required: register only after the validated runtime settings and real provider
are available, route intents into the session coordinator, surface registration
failures in readiness, prove physical push-to-talk release and mouse buttons, and
record shortcut-to-listening latency in the packaged app.

### 4. Target inspection

Implemented in `src/Soltex.Whisper.Windows`. `IWhisperTargetInspector` now returns a
nullable `WhisperTargetSnapshot` so identity and capabilities come from one atomic
inspection rather than from independently timed reads. `WindowsWhisperTargetInspector`
runs UI Automation provider work on one background MTA thread and applies one total
750 ms deadline. If a provider is slow, throws, changes focus during inspection, or
cannot expose a stable identity and integrity level, the result is unknown. Only one
native inspection can remain in flight, which prevents a non-responsive provider
from creating an unbounded thread or queue.

The native adapter reads process ID/name, token integrity level, opaque runtime ID,
enabled/focusable/password state, control type, and advertised Value, Text, and
TextPattern2 support. It queries Value/Text read-only and selection metadata only for
non-password controls. It never requests UIA Name, a Value value, text-range text,
selected text, captions, or surrounding text. Password providers are not opened.
`WhisperTargetCapabilities` carries only boolean pattern metadata; browser, editor,
terminal, plain-text, and rich-text categories remain content-free. Soltex stays
`asInvoker` with `uiAccess=false`; high/system/protected targets are identified and
the existing delivery policy falls back to copy.

Target-specific coverage is included in the current 28-case Windows adapter suite.
It covers all five target categories, pattern capability mapping,
protected/read-only/unknown controls, provider timeout, provider failure, and
cancellation. An opt-in owner-host run at commit `d11edec` also inspected a real
focused rich-text control in 72.04 ms and reported zero content reads. This is
adapter proof, not the full Win32/WPF/WinUI/Chromium/Electron/Terminal/elevated
matrix and not shipped session wiring. Context reads remain off; a future visible
per-application permission must precede any bounded contextual read.

### 5. Insertion

Implemented in the provider-neutral core and `src/Soltex.Whisper.Windows`, but not
yet wired into the shipped session. `WhisperInsertionPolicy` reauthorizes the core's
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

The core suite has 97 cases and the Windows suite has 39 deterministic cases. The
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

Implemented in the provider-neutral core and `src/Soltex.Whisper.Windows`, but not
yet wired into the shipped session. After a dispatched insertion,
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

Snippet, vocabulary, style, and per-application permission editors; history with
per-entry delete and clear; the Scratchpad; provider and device selection; the
first-use auto-send warning. Every enabled control must execute. Unavailable
capability is an explicit state, not a disabled control that looks operable.

At narrow window widths all core controls stay reachable without horizontal
scrolling. Keyboard-only operation and screen-reader labelling are requirements, not
follow-ups.

### 8. Retention

Optional disk retention stays off by default, uses the existing authenticated state
boundary plus DPAPI, exposes a retention period, and supports immediate deletion.
Transcript and audio content must not appear in Activity, diagnostics, crash text,
CI logs, or Windows event logs.

### 9. Threat model

Extend `docs/THREAT_MODEL.md` for global-hook abuse and accidental keylogging,
microphone consent and stale capture, provider credential and data exposure,
context injection, protected fields, clipboard races and exfiltration, focus
time-of-check/time-of-use changes, integrity-boundary and elevated applications,
terminal command execution, accidental submission, retention on multi-user machines,
malicious or inaccessible automation providers, and denial of service from long
audio or oversized text.

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
