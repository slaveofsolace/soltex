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
| Electron Chromium control | Implemented | Passed | Electron 42.7.1 local fixture classified `Browser`; clipboard insertion; target-owned read-back; one authorized Enter; exact process tree and profile removed |
| WinUI editable control | Implemented | Passed | Unpackaged WinUI 3 `TextBox` classified `PlainText` with framework `XAML`; whole-value automation replacement; target-owned read-back; one authorized, target-received Enter |
| Windows Terminal | Implemented | Passed | A uniquely titled owner window classified `Terminal`; normal auto-send remained `InsertText`; the separate terminal opt-in produced `InsertAndSubmit`; the harness emitted zero Enter events |
| Elevated editor | Fail-closed policy implemented | Passed | Exact UAC-approved child had a `High` token; standard-integrity UI Automation returned `UnknownTarget`; delivery reduced to `CopyText` with zero mutation or submit dispatch |
| Unknown or inaccessible provider | Fail-closed policy implemented | Deterministic coverage passed | Times out or returns unknown without guessing, inserting, or submitting |

## Verified-submission boundary

The production submitter re-inspects the target, performs a bounded read-back, calls
`WhisperInsertionVerifier`, re-inspects immediately before submission, and passes the resulting facts
to `WhisperSubmitGate`. Enter authorization is one-use and is consumed immediately before one
Enter-down/Enter-up dispatch. The current foreground process must still match the final inspected
target. Modifier state, cancellation, failed read-back, altered text, identity
drift, missing first-use consent, or an unavailable final target denies submission.

A successful `SendInput` return value is dispatch evidence, not insertion proof. If read-back is
unavailable, Whisper may leave text inserted or copied but does not submit it.

## Owner matrix status

No target-family row remains outstanding in this matrix. The WinUI row passed at exact commit
`d6704a9`: the controlled target reported framework `XAML`, `AutomationValue` insertion, verified
target-owned read-back, and exactly one received Enter key-down with `content_logged=0`. This does
not prove behavior for every control implementation or application version.

The Chromium row is opt-in and never runs in hosted CI. Set
`SOLTEX_RUN_WHISPER_CHROMIUM_TARGET_MATRIX=1`; optionally point
`SOLTEX_WHISPER_CHROMIUM_PATH` at an owned `chrome.exe` or `msedge.exe`; then run the Windows adapter
test executable on an interactive desktop. The harness creates one isolated local fixture and profile,
records the exact browser PID, never opens remote content, stops only that owned process tree, and
requires the exact temporary root to be removed before the test passes.

The Electron row is also opt-in and requires an owner-selected `electron.exe` through
`SOLTEX_WHISPER_ELECTRON_PATH`. Run the adapter executable with `--live-electron-target`. The harness
creates a network-blocked local application and isolated profile, enables renderer accessibility for
the fixture only, records the exact process, and removes its exact-owned application root.

Run `--live-terminal-target` for the Windows Terminal policy row. The harness creates a uniquely
titled window, closes only that exact HWND, and deliberately dispatches no command. Run
`--live-elevated-target` with an owner-selected .NET host through `SOLTEX_WHISPER_DOTNET_PATH` for the
cross-integrity row; the UAC-approved child is metadata-only and receives no insertion or submit.

Run `--live-winui-target` with `SOLTEX_WHISPER_WINUI_TARGET_PATH` pointing at the exact owner-built
`Soltex.Whisper.WinUiTarget.exe`. The harness starts that executable directly, verifies its process
identity and XAML automation provider, requires stable foreground focus, and closes only its owned
process. The target exposes a content-free Enter receipt count so `SendInput` acceptance alone is
never treated as delivery proof.
