# Codex implementation prompt — Soltex Whisper

Use this prompt in Codex against `https://github.com/slaveofsolace/soltex`.

## Role and working branch

You are completing a production-quality Windows dictation feature named **Whisper** inside Soltex. Work from branch `feat/soltex-whisper-scaffold-v1`, which is stacked on `sol/soltex-product-rebuild` / draft PR #11. Do not rewrite or bypass the product-rebuild architecture. Preserve the repository's .NET 10 WPF, nullable, latest-recommended analyzer, warnings-as-errors, deterministic-build, accessibility, evidence, and packaging rules.

Begin by reading:

- `docs/WHISPER_TOOL_SPEC.md`
- every file in `src/Soltex.Whisper`
- every file in `tests/Soltex.Whisper.Tests`
- `src/Soltex.App` navigation, view, theme, settings, accessibility, activity, and composition patterns
- `.github/workflows/windows.yml`, package-smoke workflows, render/evidence scripts, and `Directory.Build.props`

Run the existing build and tests before changing code. Keep each commit narrow and leave the branch buildable.

## Objective

Implement clean-room public functional parity with the externally observable desktop behaviors of **Wispr Flow**, under the Soltex feature name **Whisper**. The result must let a Windows user dictate into ordinary applications, clean and format the transcript, recover it, and optionally submit it through a shortcut or the terminal spoken phrase `press enter`.

Public behavior references:

- `https://wisprflow.ai/`
- `https://docs.wisprflow.ai/articles/4816967992-how-to-use-command-mode`
- `https://docs.wisprflow.ai/articles/6391241694-use-flow-hands-free`
- `https://docs.wisprflow.ai/articles/5298382595-route-dictation-directly-to-slack-email-or-calendar-with-keyboard-shortcuts`
- `https://docs.wisprflow.ai/articles/2368263928-how-to-setup-flow-styles`
- `https://docs.wisprflow.ai/articles/7971211038-fix-text-not-pasting-after-dictation`

Treat those pages as a behavior checklist only. Do not copy Wispr source code, private APIs, prompts, models, assets, sounds, telemetry, trademarks, product copy, or distinctive visual layout. Do not call the feature Wispr or Flow anywhere in the shipped UI. Do not imply affiliation. Implement the behavior independently with Soltex visual language and provider-neutral contracts.

## Existing scaffold: preserve and extend

The scaffold already defines:

- `IWhisperCaptureSource`, `IWhisperTranscriber`, `IWhisperTargetInspector`, `IWhisperTextDelivery`, and `IWhisperShortcutHost`;
- bounded audio, transcript, snippet, shortcut, and history models;
- session states and 19-minute warning / 20-minute hands-free expiry;
- shortcut validation: up to 4 bindings per action and up to 3 keys per binding;
- defaults for push-to-talk, hands-free, Command Mode, paste last, copy last, cancel, and Scratchpad;
- exact snippet expansion;
- deterministic spoken punctuation, paragraph, line, bullet, `scratch that`, and `delete that` handling;
- end-only, case-insensitive `press enter` parsing, including phrase-only submission;
- global, exact per-process, terminal-specific, password, unknown-target, read-only, and elevation submission policy;
- bounded in-memory transcript history;
- a coordinator joining processing, delivery planning, and optional history;
- a dedicated core workflow and named policy tests.

Do not weaken these boundaries. Replace a scaffold component only when the replacement is demonstrably more complete and its tests remain.

## Required product behavior

### 1. Push-to-talk

- Default Windows chord: `Ctrl+Win`.
- Start capture on a confirmed chord-down transition; stop on release.
- Do not repeatedly start while keys auto-repeat.
- Release finalizes audio, transcribes, cleans, and inserts into the control that was focused when the session began, provided that target is still valid.
- `Esc` cancels capture, provider work, processing, and pending delivery until the irreversible submit operation begins.
- Expose alternative bindings and mouse buttons through the shortcut editor.

### 2. Hands-free

- Default chord: `Ctrl+Win+Space`.
- Toggle recording with the configured chord.
- Support the public double-tap push-to-talk lock behavior without creating duplicate sessions.
- Show elapsed time, warn at 19 minutes, and stop safely at 20 minutes.
- Stopping finalizes and inserts. Canceling discards active audio but retains the previous completed transcript.

### 3. Command Mode

- Default chord: `Ctrl+Win+Alt`.
- Hold to issue a command; release to execute it.
- When accessible selected text exists, transform that selection only.
- Without a selection, insert the command response at the caret.
- Support at minimum: rewrite, shorten, lengthen, summarize, make formal, make casual, fix grammar, convert to bullets, and translate to a user-selected language.
- Always preview destructive or meaning-changing transforms. Never execute shell commands or application actions from natural-language Command Mode.

### 4. Shortcut system

- Allow up to 4 bindings per action and up to 3 keys per binding.
- Support keyboard keys plus Mouse 4 and Mouse 5.
- Detect conflicts independent of key order.
- Reject unsafe or reserved Windows chords with a clear reason.
- Register and unregister atomically. If one binding fails, preserve the previous valid set.
- Keep the global hook callback minimal; queue work to managed code and never capture unrelated keystroke content.
- Add a dedicated `SubmitLastTranscript` action. It must still pass all target and authorization checks.

### 5. Listening surface

Create a compact, native WPF overlay using Soltex design tokens. States: idle-hidden, listening, locked hands-free, transcribing, cleaning, ready, inserting, submitted, copied fallback, canceled, and actionable error.

Requirements:

- no generic glass dashboard, gradients, decorative telemetry, or copied Wispr visuals;
- visible microphone and target application identity;
- elapsed time for hands-free;
- one clear cancel action;
- optional waveform or level meter that is throttled and does not retain audio;
- keyboard reachability, logical focus order, AutomationProperties names/help text, high contrast, 200% scaling, reduced motion, and screen-reader announcements for state changes;
- no transcript text in the always-on overlay unless the user enables preview.

### 6. Dictation cleanup and formatting

Implement a deterministic local pass plus an optional provider polish pass. The local pass must remain usable when the provider is offline.

Required behavior:

- spoken punctuation, new line, new paragraph, bullets, parentheses, and common symbols;
- remove supported filler and duplicated fragments conservatively;
- support immediate corrections such as `scratch that` and `delete that` without deleting unrelated clauses;
- preserve names, numbers, URLs, email addresses, code, and casing when confidence is uncertain;
- never silently invent facts, recipients, dates, commands, or commitments;
- provide a visible raw-versus-final comparison in history for the current session when the user enables it;
- terminal and developer styles can disable prose cleanup and preserve literal tokens.

### 7. Snippets, dictionary, languages, and styles

- Snippets use an exact spoken cue and insert stored content without reformatting that content.
- Reject duplicate cues case-insensitively.
- Add create, edit, disable, delete, import, export, and test controls.
- Personal dictionary entries must be bounded, normalized, deduplicated, and passed to the transcriber as hints—not blindly substituted after transcription.
- Support explicit language selection, auto-detect, and provider-reported language. Preserve mixed-language speech when the provider supports it.
- Add per-application styles selected by exact process profile, plus general categories such as message, email, document, terminal, and developer.
- Style rules must be user-readable, bounded, and independently disableable. Do not store hidden prompts that the user cannot inspect.

### 8. Target inspection and insertion

Implement `IWhisperTargetInspector` with Windows UI Automation. Record a stable target descriptor at session start and revalidate immediately before insertion and again before submission.

Inspect only what is required:

- process identity and integrity level;
- focused element runtime identity;
- editable/read-only state;
- password/protected-entry state;
- supported ValuePattern, TextPattern, TextPattern2, selection, and caret capabilities;
- target category: plain text, rich text, terminal, browser/editor fallback, or unknown.

Never read password values. Never log surrounding text. Context access is off by default and must have a visible per-application permission.

Delivery order:

1. use a direct supported automation value or selection operation when it preserves expected editing semantics;
2. otherwise use a clipboard paste fallback with a unique operation token and clipboard-sequence tracking;
3. restore the prior clipboard only when Whisper still owns the sequence and the user enabled restoration;
4. if the target is unknown, read-only, protected, stale, inaccessible because of UIPI/elevation, or changed during the operation, copy the finalized text and explain the fallback;
5. never type character-by-character through broad synthetic input.

### 9. Guarded auto-send

Automatic submission is opt-in and fail-closed. A request to submit may originate only from:

- the terminal spoken phrase `press enter`, recognized case-insensitively only at the end of the final transcript and removed before insertion; or
- the dedicated submit shortcut.

The phrase by itself means submit-only. The phrase in the middle remains ordinary dictated text.

Before emitting Enter, all conditions must hold:

1. global auto-send is enabled;
2. first-use warning has been accepted;
3. the exact focused process has an enabled profile;
4. the element is positively identified as editable and not read-only;
5. the element is not a password or protected-entry control;
6. the target runtime identity still matches the captured target;
7. Soltex is permitted to interact at the target integrity level;
8. insertion has completed and is verified through readable UI Automation state or another deterministic application adapter;
9. the exact inserted text matches the finalized transcript, allowing only documented rich-text normalization;
10. no cancellation, focus change, provider failure, timeout, or clipboard ownership loss occurred;
11. terminal targets have a second terminal-specific opt-in.

If verification is unavailable, insert or copy but do not submit. Do not treat a successful `SendInput` call as proof that insertion occurred. Emit Enter once, after a short bounded settle/revalidation window, and record content-free evidence: target category, requested origin, policy decision, verification method, and outcome.

Whisper must not silently choose a recipient, channel, conversation, email address, or terminal. Named destination shortcuts may launch/focus an owner-configured application and then require a confirmed editable target. Any future automatic recipient selection requires a separate threat model and explicit preview.

### 10. Transcript recovery

- Default history is session-memory only and bounded.
- Paste last and copy last use the last completed transcript, never a partial transcript.
- Keep the previous completed transcript if a new session is canceled or fails.
- Add clear-history and per-entry delete.
- Optional disk retention must be off by default, use the existing Soltex authenticated state plus Windows DPAPI, expose a retention period, and support immediate deletion.
- Transcript or audio content must not appear in Activity, diagnostics, crash text, analytics, CI logs, or Windows event logs.

### 11. Scratchpad

Create a detachable but native Soltex Scratchpad with up to 5 tabs, autosave only when encrypted retention is enabled, undo/redo, version snapshots, copy, insert into current target, dictate into tab, and Command Mode transforms. It must work fully by keyboard and expose document changes to assistive technology. Do not turn it into a general note-sync service.

### 12. Capture and provider architecture

Implement microphone capture behind `IWhisperCaptureSource` using a Windows WASAPI path or one narrowly vetted lightweight dependency. Requirements:

- user-selected input device;
- mono PCM normalization suitable for the provider;
- device-change and disconnect recovery;
- no loopback/system-audio capture;
- bounded buffering and cancellation;
- raw audio deleted after success, failure, or cancellation;
- level metering separated from retained audio;
- clear permission/device errors.

Keep transcription behind `IWhisperTranscriber`. Add at least one real adapter and one deterministic fake adapter. Provider settings must include endpoint/model identity, language capability, streaming capability, privacy/storage statement, and credential availability. Credentials must use the repository's secret-storage boundary and never source files, plain JSON, process arguments, or logs.

A provider may return transcription and optional polish metadata, but it must never issue clicks, keypresses, application commands, or submission decisions. The deterministic Soltex policy remains the sole actuator authority.

### 13. Soltex WPF integration

Add Whisper as a first-class Soltex tool without destabilizing other surfaces:

- navigation entry and page;
- status summary: microphone, shortcut registration, provider, target access, history mode, and auto-send state;
- Dictation, Shortcuts, Snippets, Dictionary, Styles, History, Scratchpad, Privacy, and Advanced sections using the existing navigation conventions rather than a dense tab wall;
- explicit unavailable/error states instead of disabled controls that appear functional;
- light/dark/high-contrast token use;
- no transcript content in the global Activity feed;
- startup registration only after settings validation;
- clean shutdown that unregisters hooks, cancels capture/provider work, clears ephemeral audio, and disposes native resources.

At 320 CSS-equivalent pixels / narrow WPF width, all core controls must remain reachable without horizontal scrolling. Test keyboard-only operation and screen-reader labels.

### 14. Settings and migrations

Persist a versioned `WhisperSettings` document using existing Soltex settings infrastructure. Include enabled state, input device ID, shortcut set, provider selection, preferred language, snippets, dictionary, style profiles, per-process permissions, auto-send global consent, terminal consent, context permission, clipboard behavior, history mode, retention, and preview settings.

Validate every field, cap collection sizes and string lengths, reject control characters and paths where names are expected, migrate older versions deterministically, and fall back to safe defaults without losing the recoverable invalid file. Auto-send, context reads, terminal submission, and disk history must default off.

### 15. Diagnostics and threat model

Extend Soltex evidence without logging content. Add structured content-free events for session state, duration bucket, provider result category, shortcut registration, target category, insertion method, fallback reason, submit policy decision, and cancellation. Redact exception messages that may include provider payloads.

Add or update the repository threat model for:

- global-hook abuse and accidental keylogging;
- microphone consent and stale capture;
- provider credential/data exposure;
- prompt/context injection;
- protected fields;
- clipboard races and clipboard exfiltration;
- focus/TOCTOU changes;
- UIPI and elevated applications;
- terminal command execution;
- accidental message/email submission;
- transcript retention and multi-user Windows machines;
- malicious or inaccessible UI Automation providers;
- denial of service from long audio or oversized text.

## Tests and evidence

Keep the current core suite and add:

### Unit and property tests

- all valid/invalid session transitions;
- shortcut normalization, bounds, duplicate/conflict detection, atomic rollback, and mouse keys;
- `press enter` casing, punctuation, phrase-only, middle-position, false-positive, and Unicode boundaries;
- snippets, dictionary normalization, language/style selection, formatting, backtrack, and size limits;
- every auto-send allow/deny branch;
- password, read-only, unknown, stale, elevated, and terminal targets;
- clipboard ownership/restore races;
- history retention and deletion;
- cancellation at every pipeline state;
- settings validation and migrations.

### Windows integration harness

Build owner-controlled sample targets for Win32 Edit, WPF TextBox/RichTextBox/PasswordBox, WinUI, Chromium contenteditable/input, Electron, Office-like rich text if available, Windows Terminal, an elevated editor, a read-only field, and a target that changes focus during insertion. The harness must expose deterministic evidence without capturing user content.

### Accessibility and visual evidence

- keyboard walkthrough;
- Narrator/Accessibility Insights proof;
- 100%, 150%, 200%, and high-contrast renders;
- narrow-width render;
- reduced-motion behavior;
- overlay states and actionable errors.

### Performance evidence

Measure and record, without transcript content:

- idle private working set and CPU with Whisper enabled;
- hook callback p50/p95/p99 duration;
- capture buffer bounds;
- shortcut-to-listening latency;
- release-to-first-provider-request latency;
- insertion and verified-submit latency;
- cleanup/disposal after cancellation and shutdown.

Do not claim exact latency targets as passed until measured on an owner-controlled Windows host. Avoid polling loops, unbounded queues, per-frame allocations, or keeping the microphone active while idle.

### CI

Integrate the Whisper project/tests into the canonical Windows gate after the standalone workflow is green. Add content-free build/test/runtime/accessibility evidence. Update package-smoke and installer verification so the feature's assemblies, settings migration, shortcut startup, and clean uninstall are proven. Keep hosted-CI limitations classified honestly; owner-host UI Automation, microphone, elevation, and live provider proof cannot be replaced by mocks.

## Definition of done

Do not call Whisper complete until all of the following are true:

- Release build has zero warnings and errors;
- core, settings, provider, Windows integration, accessibility, and package tests pass;
- push-to-talk, hands-free, Command Mode, cancel, paste/copy last, snippets, dictionary, languages, styles, history, Scratchpad, and per-app profiles work in the shipped UI;
- microphone and provider failures recover without losing the previous transcript;
- global hooks register/unregister cleanly and do not capture unrelated key content;
- insertion is proven across the supported target matrix;
- auto-send remains off by default and Enter is never emitted without full authorization and insertion verification;
- password, read-only, unknown, stale, elevated, and terminal cases fail closed as specified;
- transcript/audio content is absent from logs and evidence;
- light, dark, high contrast, narrow width, keyboard, and screen-reader checks pass;
- package/install/update/uninstall evidence includes Whisper;
- owner-controlled live proof exists for at least one real provider and the supported Windows target matrix;
- documentation states actual limitations and does not imply proprietary Wispr code or affiliation.

## Required final handoff

Return:

1. branch, commit SHAs, and PR;
2. exact files changed and architecture diagram in text;
3. completed public-parity matrix with `implemented`, `partially implemented`, `unavailable`, or `owner proof pending` for every capability;
4. build/test/evidence results with commands and artifact paths;
5. measured performance and resource results;
6. security/privacy decisions and remaining risks;
7. supported/unsupported application matrix;
8. screenshots or render artifact paths for all required themes/scales/states;
9. known defects and the smallest coherent next slice;
10. explicit confirmation that no proprietary Wispr implementation, assets, or branding were copied.

Do not hide unfinished behavior behind optimistic copy. Every enabled control must execute, and every completion claim must be backed by runtime evidence.
