# Whisper owner acceptance center

## Scope

Source commit: `e25c49b9cde48a970b2d875d168adf9984acfc1c`

This slice adds a session-only Checks tab for the six observations that cannot
be replaced honestly by deterministic CI:

1. a physical push-to-talk shortcut and shortcut-to-listening latency;
2. an owner-spoken insertion confirmed by target-owned read-back;
3. physical Escape cancellation and owned-session drain;
4. selected-microphone disappearance and bounded rediscovery;
5. a live Windows per-monitor DPI transition; and
6. an explicit keyboard-only and screen-reader walkthrough.

The tracker accepts only fixed policy outcomes and one bounded millisecond
measurement. It does not accept or retain key content, audio, transcript text,
target text, process names, paths, or credentials. Results live only for the
current process. The navigation `SCAFFOLD` marker hides only after all six checks
pass in that process.

## Automated verification

The exact source tree was built in Release with the repository's private .NET
10 SDK:

- build: 0 warnings, 0 errors;
- provider-neutral Whisper tests: 119/119 passed;
- application tests: 50/50 passed; and
- Windows adapter tests: 62/62 passed.

The focused tests cover arming one check at a time, unrelated-shortcut
rejection, measured physical-shortcut completion, verified versus unverified
insertion, physical versus nonphysical cancellation, microphone
disconnect/reconnect order, live-DPI observation, explicit assistive-technology
confirmation, reset, enabled UI actions, and `SCAFFOLD` visibility.

The exact local PowerShell design-token gate exceeded its 60-second owned
process bound without producing output. No second shell-family probe was run.
A direct equivalent of the three repository contracts reported:

- 12 tracked application XAML files scanned with 0 raw-colour violations;
- 309 tracked or pending text files scanned against 8 reasoned identity
  allowlist entries with 0 violations; and
- 152 interactive controls across 13 application XAML files with 0
  accessibility-contract failures.

This equivalent is supporting local evidence, not a substitute for the exact
hosted PowerShell gates. The exact hosted Windows workflows remain authoritative
after push.

## Render evidence

Valid `whisper*` render requests now use an isolated evidence startup. They do
not enumerate microphones, hash the installed 547 MiB model, or query unrelated
live workspace integrations. Normal application startup and non-Whisper render
contracts are unchanged.

Two development renders exposed and bounded the issue: one 30-second attempt
produced a late frame, and one 20-second retry produced no frame. Each exact
owned process tree was stopped at its deadline and no Soltex process remained.
After isolation, every render completed within the 20-second repository bound.

| Appearance and profile | Pixels | SHA-256 |
| --- | ---: | --- |
| dark, standard 100% | 1280x820 | `809543e3182cd4016b0f90899b1185c92730d8a25d73b3cf63b1ffc249e2aa82` |
| light, standard 100% | 1280x820 | `357b30f9734ae4fc9170d48e5699c9e40ffd835be0a7179e35fa5f6976cb52e3` |
| high contrast, standard 100% | 1280x820 | `ae5a9beab94ae87b04be8d5673b878dfdfbdd18693efed885959f2bbe4fe0fee` |
| dark, compact 100% | 1100x720 | `c69f3ca9feffd4cea9efa187b8a25328310a20be766686a2d1a45a8cbe9e724d` |
| dark, compact 150% | 1650x1080 | `0a1f9109e821e5fa6669c90bc2918260e1efcf6af0eeeb66f319d06755f49cd5` |
| dark, compact 200% | 2200x1440 | `4818751c18f2eee330aa17cb413b838171e920c2c6bbcc4fc257ae7cf271ecde` |

Human inspection found and corrected one checklist title/body collision. The
final dark, high-contrast, and 200-percent frames were inspected with no title
collision, horizontal clipping, hidden check, or raw-colour fallback.

The content-free local manifest is
`artifacts/whisper-stage6/2026-08-20-stage6-owner-acceptance-evidence.json`
(SHA-256
`060a4118864eec90e9a0db5a9dc23f6d5e205f4f5e6bf5ec4a2d51cca864b36d`).
The ignored manifest binds the build, tests, policy-equivalent report, six PNGs,
exact byte sizes, and hashes to the source commit above.

## Remaining owner gates and nonclaims

All six Checks rows remain pending until the owner performs them in the shipped
app. In particular:

- deterministic shortcut tests do not prove a physical keyboard or mouse;
- the installed Turbo Q5 package proof does not prove owner-spoken accuracy or
  capture-to-verified-insertion behavior;
- 150/200-percent raster output does not prove a live monitor transition;
- accessible names and keyboard contracts do not replace a screen-reader
  walkthrough; and
- deterministic microphone recovery does not prove a physical disconnect and
  reconnect across representative devices.

Stage 6 therefore has a working acceptance workflow and supporting automated
evidence, but the owner-observed completion gate is still pending.
