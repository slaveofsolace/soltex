# Whisper target matrix

This matrix records what the production Windows adapters support and what has been exercised on an
owner-controlled desktop. A deterministic test proves policy and adapter behavior; it does not
replace a real application provider. An owner-host result applies only to the named control family.

No target text, dictated text, clipboard content, window caption, recipient, channel, address, or
terminal command is written to the evidence log. Controlled fixtures use fixed local text and report
only categories, methods, decisions, timing, and counts.

| Target | Adapter state | Owner-host evidence | Current result |
|---|---|---|---|
| Win32 Edit / WinForms TextBox | Implemented | Passed | Clipboard paste with ownership-checked restore; whole-value automation replacement; target-owned read-back; one authorized Enter |
| WPF TextBox | Implemented | Passed | Classified `PlainText`; whole-value automation replacement |
| WPF RichTextBox | Implemented | Passed | Classified `RichText`; one clipboard paste with ownership-checked restore |
| WPF PasswordBox | Implemented | Passed | Protected metadata only; no provider object is opened; insertion policy returns `ProtectedField` copy fallback |
| WPF read-only TextBox | Implemented | Passed | Read-only metadata preserved; insertion policy returns `TargetNotEditable` copy fallback |
| Focus-changing WPF target | Implemented | Passed | Runtime identity drift detected before mutation; insertion policy returns `TargetChanged` copy fallback |
| Chromium input and contenteditable | Implemented | Passed | Framework/process classification returns `Browser`; clipboard paste; target-owned read-back; one authorized Enter per controlled target |
| Electron Chromium control | Implemented classification | Owner proof pending | Bounded `Chrome`/`Chromium` framework metadata maps a non-browser process to `Browser`; known editor processes retain `Editor` precedence |
| WinUI editable control | Core/adapter path implemented | Owner proof pending | No WinUI sample-provider result is claimed |
| Windows Terminal | Core/adapter path implemented | Owner proof pending | Terminal process classification and the separate terminal-submit opt-in are deterministic; no Windows Terminal mutation is claimed |
| Elevated editor | Fail-closed policy implemented | Owner proof pending | High, system, and protected integrity levels reduce to copy; no UAC/elevated live run is claimed |
| Unknown or inaccessible provider | Fail-closed policy implemented | Deterministic coverage passed | Times out or returns unknown without guessing, inserting, or submitting |

## Verified-submission boundary

The production submitter re-inspects the target, performs a bounded read-back, calls
`WhisperInsertionVerifier`, re-inspects immediately before submission, and passes the resulting facts
to `WhisperSubmitGate`. Enter authorization is one-use and is consumed immediately before one
Enter-down/Enter-up dispatch. Modifier state, cancellation, failed read-back, altered text, identity
drift, missing first-use consent, or an unavailable final target denies submission.

A successful `SendInput` return value is dispatch evidence, not insertion proof. If read-back is
unavailable, Whisper may leave text inserted or copied but does not submit it.

## Remaining owner matrix

- a controlled WinUI text target;
- a controlled Electron application, distinct from Chromium framework-unit coverage;
- Windows Terminal with submission disabled and with the separate terminal opt-in exercised safely;
- a controlled elevated editor confirming the cross-integrity copy boundary;

These rows remain incomplete until their owner-controlled runs produce content-free evidence from the
exact commit under review.

The Chromium row is opt-in and never runs in hosted CI. Set
`SOLTEX_RUN_WHISPER_CHROMIUM_TARGET_MATRIX=1`; optionally point
`SOLTEX_WHISPER_CHROMIUM_PATH` at an owned `chrome.exe` or `msedge.exe`; then run the Windows adapter
test executable on an interactive desktop. The harness creates one isolated local fixture and profile,
records the exact browser PID, never opens remote content, stops only that owned process tree, and
requires the exact temporary root to be removed before the test passes.
