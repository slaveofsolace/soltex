# Soltex Whisper Tool Specification

Status: **scaffolded core; no live microphone, cloud transcription, global hook, focused-control injection, or WPF surface is claimed yet**

Reference product: **Wispr Flow**  
Soltex feature name: **Whisper**

## 1. Scope and clean-room boundary

The goal is externally observable functional parity with the public behavior of Wispr Flow's desktop dictation product, adapted to Soltex's native Windows architecture.

"Parity" does not mean copying Wispr's source code, private APIs, models, prompts, assets, sounds, trademarks, telemetry, or distinctive visual layout. Implementation must be clean-room, provider-neutral, and consistent with Soltex's existing Quiet Instrument Deck design and evidence standards.

Primary public references:

- https://wisprflow.ai/
- https://docs.wisprflow.ai/articles/4816967992-how-to-use-command-mode
- https://docs.wisprflow.ai/articles/6391241694-use-flow-hands-free
- https://docs.wisprflow.ai/articles/5298382595-route-dictation-directly-to-slack-email-or-calendar-with-keyboard-shortcuts
- https://docs.wisprflow.ai/articles/2368263928-how-to-setup-flow-styles
- https://docs.wisprflow.ai/articles/7971211038-fix-text-not-pasting-after-dictation

The public references are a behavior checklist, not a license to reuse protected implementation details.

## 2. Product contract

Whisper is a Windows-wide speech-to-text tool that:

1. starts from a global shortcut or a visible Soltex control;
2. captures microphone audio with a bounded session;
3. transcribes through a replaceable provider;
4. removes filler, repetition, and supported self-corrections;
5. formats the result for the focused application;
6. inserts it into a confirmed editable control;
7. optionally submits it only after explicit global and per-application authorization;
8. preserves a recoverable last transcript without exposing it in logs;
9. remains cancelable through Escape until the final submit action begins.

The default posture is **dictate, review, insert**. Automatic submission is an additional, explicit capability.

## 3. Public parity matrix

| Capability | Required Soltex behavior | Current scaffold |
|---|---|---|
| Push-to-talk | Hold shortcut, capture while held, release to transcribe and insert | Shortcut model and state machine only |
| Hands-free | Toggle recording; support push-to-talk double-tap lock; warn at 19 minutes and stop at 20 | Duration policy and default shortcut only |
| Command Mode | Hold a separate shortcut; operate on selected text when available; otherwise insert an inline answer | Contract and shortcut only |
| Global shortcuts | Up to 4 bindings per action, up to 3 keys per binding, conflict detection, mouse-button support, blocked unsafe OS chords | Deterministic binding/set validation; OS host pending |
| Cancel | Escape cancels active capture, transcription, processing, or pending delivery | Session cancellation contract; OS host pending |
| Smart cleanup | Remove filler, repetition, false starts, and supported corrections without changing meaning | Deterministic spoken punctuation and one-token backtrack baseline |
| Smart formatting | Punctuation, paragraphs, lists, email/message structure, app-aware formatting | Spoken punctuation, line, paragraph, and bullet baseline |
| Snippets | Exact spoken cue expands to stored text without reformatting the stored content | Implemented |
| Personal dictionary | User terms and learned corrections improve transcription | Provider context contract pending |
| Multiple languages | Explicit language selection plus auto-detect; preserve code-switching where supported | Provider context contract pending |
| Per-app styles | Different tone/format rules by application category or exact process | Profile model implemented; provider/UI pending |
| Context awareness | Read only the minimum supported focused-control context needed to format correctly | Target-inspector contract pending |
| Auto-paste | Insert into the focused editable control; restore clipboard after fallback paste | Delivery decision and restore intent implemented; Windows delivery pending |
| Paste/copy last | Dedicated shortcuts recover the last finalized transcript | Shortcut actions and bounded in-memory history implemented |
| Transcript history | Recover recent transcripts; default session-only; optional encrypted retention | Bounded in-memory history implemented |
| Scratchpad | Floating editor, up to 5 tabs, version history, copy, transforms, formatting controls | Pending |
| "Press enter" | Recognize case-insensitively only at the end; strip the phrase; support phrase-only Enter; first-use opt-in | Parser and guarded delivery policy implemented; first-use UI pending |
| Dedicated auto-send shortcut | Finalize and submit to a confirmed, allowlisted focused target | Policy and shortcut action implemented; Windows delivery pending |
| Terminal behavior | Preserve literal/code formatting; terminal auto-submit needs an extra opt-in | Separate terminal authorization implemented |
| Developer context | Variable/file-name vocabulary, optional selected-file tags, syntax-preserving mode | Pending |
| App bar/overlay | Compact listening/transcribing/error surface, keyboard and screen-reader accessible | Pending |
| Privacy controls | No content logs; explicit storage/training/provider controls; bounded local state | Core avoids logging; provider/storage UI pending |

## 4. Implemented scaffold

`src/Soltex.Whisper` currently contains:

- bounded product limits;
- capture, transcriber, focused-target, delivery, and shortcut-host interfaces;
- session states for Idle, Listening, Transcribing, Processing, Delivering, Completed, Cancelled, and Faulted;
- 19-minute warning and 20-minute hands-free expiry policy;
- shortcut validation with a maximum of 4 bindings per action and 3 keys per binding;
- public Windows default shortcut definitions;
- end-only `press enter` parsing;
- exact snippet expansion;
- deterministic spoken punctuation, line, paragraph, bullet, and minimal backtrack processing;
- global + per-app + terminal-specific submission authorization;
- fail-closed password behavior;
- copy-only fallback for unknown, read-only, or inaccessible elevated targets;
- bounded session-memory transcript history;
- a coordinator that combines finalization, delivery planning, and optional history.

The scaffold deliberately contains no synthetic keyboard input and no network provider.

## 5. Submission safety contract

Auto-send must satisfy every condition below:

1. the user enabled auto-send globally;
2. the exact focused process has an enabled profile;
3. the focused element was positively identified as editable and not read-only;
4. the element is not a password or protected-entry control;
5. Soltex can legally interact at the target's integrity level;
6. the focused target remains stable from inspection through insertion;
7. submission was requested by an end-only spoken command or a dedicated shortcut;
8. terminal targets have a second terminal-specific opt-in;
9. Escape has not canceled the operation;
10. insertion was verified before Enter is emitted.

A failure before item 10 must never emit Enter. Text should remain recoverable through history or an intentional copy fallback.

Whisper must not silently open a conversation, choose a recipient, send an email, post to a channel, or execute a terminal command. A named destination profile may focus or launch an owner-configured application only when the final target and action are shown before first use. Automatic recipient selection requires a separate product and threat-model review.

## 6. Data and privacy boundary

Default behavior:

- audio is held only for the active request;
- raw audio is deleted after provider completion or cancellation;
- transcript content is not written to diagnostics, Activity, crash text, or CI artifacts;
- history is session-memory only;
- provider credentials are never stored in source, JSON, command-line arguments, or logs;
- context reads are off unless the user enables them;
- password/protected fields are never read;
- clipboard fallback restores the prior clipboard only if the clipboard sequence still belongs to Whisper;
- provider requests are canceled when the session is canceled;
- no transcript is used for model training unless the chosen provider offers that control and the user explicitly selects it.

Optional disk retention must use the existing Soltex authenticated-state and DPAPI boundaries, include a visible retention period, and support immediate deletion.

## 7. Deterministic text behavior in the scaffold

The current formatter intentionally does less than the final product:

- terminal submit parsing is exact and deterministic;
- exact snippets are preserved byte-for-byte as .NET strings;
- spoken punctuation maps only from explicit commands;
- `scratch that` and `delete that` remove the immediately preceding token;
- app-aware rewriting, semantic correction, filler removal, list inference, and style rewriting require the future transcription/polish provider;
- terminal and developer profiles must be able to disable natural-language formatting.

No scaffold result should be described as full Wispr Flow-quality cleanup.

## 8. Verification boundary

The dedicated Whisper workflow must:

- build `tests/Soltex.Whisper.Tests` in Release;
- run all named core tests;
- upload the build and test logs;
- fail on warnings because the repository's global policy treats warnings as errors.

The canonical Soltex Windows workflow, WPF surface, accessibility verifier, render matrix, package-smoke workflow, runtime-cost probe, and installer are intentionally unchanged in this first slice. They become mandatory when the live tool is wired into the application.
