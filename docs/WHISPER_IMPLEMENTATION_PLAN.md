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
85-test suite in `tests/Soltex.Whisper.Tests`.

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

The WPF surfaces in `src/Soltex.App` — navigation entry, Whisper page with Setup,
Shortcuts, and Privacy tabs, and the floating listening surface — render this core.
They are honest about capability: the navigation entry is marked `SCAFFOLD` and the
readiness checklist reports capture, providers, shortcuts, and target inspection as
unavailable.

## Not implemented

Nothing below exists yet. Any claim that it works is false until runtime evidence
from an owner-controlled Windows host says otherwise.

1. Microphone capture (WASAPI), device selection, and disconnect recovery.
2. A real transcription provider adapter and its credential storage.
3. Global Windows keyboard and mouse hook registration.
4. UI Automation inspection of real focused controls.
5. Insertion into another application, and the clipboard paste fallback.
6. Emission of Enter.
7. Live level metering.
8. Scratchpad UI, snippet/vocabulary/style editors, and history UI.
9. Encrypted transcript retention.
10. Accessibility, performance, and packaging evidence for the Whisper surfaces.

## Remaining work

### 1. Capture

Implement `IWhisperCaptureSource` over WASAPI or one narrowly vetted dependency.
User-selected input device; mono PCM normalization; device-change and disconnect
recovery; no loopback or system-audio capture; bounded buffering; cancellation;
raw audio deleted on success, failure, and cancellation; level metering separated
from retained audio; distinguishable permission and device errors.

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

Register the configured set through low-level Windows hooks. Push-to-talk starts on
a confirmed chord-down transition and stops on release, without restarting under key
auto-repeat. Hands-free toggles and supports double-tap locking without creating a
second session. Registration is atomic: if one binding fails, the previous valid set
survives and the failure is surfaced through readiness. The hook callback stays
minimal, queues work to managed code, and never captures unrelated keystroke content.

### 4. Target inspection

Implement `IWhisperTargetInspector` with UI Automation, producing the
`WhisperTargetSnapshot` the core already consumes. Inspect only process identity and
integrity level, focused element runtime identity, editable and read-only state,
password state, supported patterns, and target category. Never read password values.
Never log surrounding text. Context reads stay off by default behind a visible
per-application permission.

### 5. Insertion

In order: a direct supported automation value or text operation when it preserves
editing semantics; otherwise a clipboard paste fallback with a unique operation
token and clipboard-sequence tracking; restore the prior clipboard only while
Whisper still owns the sequence and the user enabled restoration; if the target is
unknown, read-only, protected, stale, inaccessible across an integrity boundary, or
changed mid-operation, copy and explain the fallback. Never type character by
character through broad synthetic input.

### 6. Verified submission

Route every submission through `WhisperSubmitGate`. Re-inspect the target
immediately before insertion and again before submission, compare against the
captured snapshot, verify insertion by reading the target's own state, then emit
Enter exactly once after a short bounded settle window. Record content-free evidence:
target category, requested origin, policy decision, verification method, outcome.

A successful synthetic-input call is not proof that insertion occurred. If
verification is unavailable, insert or copy, but do not submit.

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
