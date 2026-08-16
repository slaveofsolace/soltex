# Soltex repository threat model

## Overview

Soltex is a proprietary Windows desktop application intended to combine audio controls, local clip capture, security visibility, verified update planning, and user-approved remote assistance. In the current repository, the primary runtime surface is the .NET 10 WPF app under `src/Soltex.App`, the Security companion under `src/Soltex.Security`, the non-installing planner under `src/Soltex.Update`, and a narrow external-client adapter under `src/Soltex.RemoteAssist`. Audio provides bounded Core Audio observation plus guarded per-app volume and mute with immediate read-back; routing, DSP, virtual endpoints, APO, and driver work are absent. Capture remains a user-visible preview with no recorder or encoder. Remote Assist does not contain a capture, transport, input, relay, or authentication implementation; it can only launch a separate RustDesk client.

The Security companion runs unelevated. It reads antivirus health, invokes supported Microsoft Defender operations, assesses Soltex-bound content, and manages a per-user quarantine. It does not provide system-wide enforcement and must coexist with the antivirus product registered with Windows.

Security-sensitive assets are user files, quarantined payloads, original restore paths, exact-hash allow decisions, metadata/release signing keys, TLS and publisher policy, signed acquisition descriptors and manifests, update monotonicity/planning state, private staged artifacts, local audit integrity, the truthfulness of displayed Windows protection health, the approved external-client fingerprint, peer IDs, remote-session authorization intent, microphone audio, provider credentials, transcript text, focused-control identity, shortcut intent, and submission consent.

## Threat Model, Trust Boundaries, and Assumptions

### Actors and inputs

- **Attacker-controlled:** downloaded presets, plugins, archives, executables, acquisition descriptors, trust-policy proposals, manifests, detached signatures, redirect targets, response lengths/bodies, file names, paths, reparse points, file contents, peer IDs supplied through untrusted channels, and files changed between validation and use.
- **Operator-controlled:** scan targets, quarantine restore/delete choices, exact-hash allow decisions, opening Windows-owned security surfaces, selecting a RustDesk client, confirming a remote launch, and verifying authorization for a peer.
- **Developer-controlled:** source, release signing keys, accepted publisher identities, manifests, CI, installer/update logic, and future driver packages.
- **Platform-controlled:** WSC health, Defender cmdlets, AMSI provider decisions, Authenticode chain policy, DPAPI, filesystem semantics, and enterprise security policy.

### Boundaries

1. **Untrusted filesystem to Soltex intake.** `PathSafety`, `FileHashing`, `FileAssessmentService`, AMSI, and future strict parsers mediate this transition.
2. **Soltex to Windows antimalware.** `WindowsSecurityCenter`, `WindowsSecurityChangeMonitor`, `ProtectionMonitor`, `PowerShellDefenderClient`, and `AmsiContentScanner` consume platform results that may be unavailable, provider-managed, stale, or policy-limited.
3. **Release infrastructure to inert update preview.** `UpdateDescriptorVerifier`, `UpdateTrustTransitionEvaluator`, `PinnedHttpsTransport`, `BoundedHttpsAcquirer`, `SignedReleaseManifestVerifier`, `BoundedArchiveStager`, and `AuthenticodePublisherVerifier` establish bounded acquisition and exact preview evidence. Production identities/source are not configured, and no installed-content transition exists.
4. **Detection to quarantine and restore.** `QuarantineStore` moves data out of its original location, authenticates metadata, verifies payload hashes, and later writes back to an authenticated original path or a collision-safe sibling.
5. **User intent to trust.** `AllowListStore` accepts exact hashes only. UI warnings are part of the control and must not be bypassed by background logic.
6. **Application to per-user state.** DPAPI and HMACs protect accidental/cross-account modification. They do not stop malware already executing as the same user from deleting state or invoking DPAPI.
7. **Security workload to future real-time workloads.** No security operation may run on audio, capture, encoder, APO, or driver threads.
8. **Soltex to external remote client.** `RemoteAssistExecutable`, `RemotePeerId`, and `RustDeskExternalClient` mediate a one-way launch request. RustDesk and its infrastructure remain a separately licensed, separately updated, network-facing trust domain.
9. **Preview to future installer.** No implementation crosses this boundary. A future privileged component must accept only the exact confirmed plan and immutable verified handles, never a URL, archive path, or generic command.
10. **Future personal device fabric.** Device agents, Tailscale reachability, NAS storage, Google Drive, Box, and AI intent translation are designed but not implemented. Before code enters this boundary, jobs must be typed, target-bound, short-lived, replay-resistant, locally authorized, and free of generic shell semantics. The NAS must have no authorization role, and personal/work cloud identities must remain isolated by default.
11. **Whisper to global input.** The shortcut adapter observes system-wide key and mouse transitions because Windows low-level hooks are global. Registration policy, configured-key filtering, transition-only dispatch, injected-input rejection, and clean unregistration constrain that boundary. The adapter never records text, key sequences, active-window content, or unrelated key identities and never suppresses input from reaching Windows.
12. **Whisper to focused application metadata.** UI Automation crosses into an untrusted provider process. Inspection is metadata-only, single-flight, deadline-bounded, and produces one identity-and-capability snapshot or unknown. Soltex does not request UIAccess, cross an integrity boundary, or read field, selection, caption, password, or surrounding text at this boundary.
13. **Whisper to target and clipboard mutation.** The core reauthorizes the captured target before Windows code acts and again after clipboard staging. Direct UIA replacement is whole-value-only; other insertion uses one paste chord. Clipboard restore requires a matching sequence number and unique operation token and never inspects prior formats.
14. **Whisper settings to global shortcut lifecycle.** The shipped host installs hooks only after versioned settings migration/validation and an explicit persisted enable action. Disable, shutdown, render evidence, registration failure, and runtime hook fault all fail closed to no active action path.
15. **Whisper to retained transcript state.** Disk history is opt-in. Transcript text crosses into current-user DPAPI only through the provider-neutral retention boundary, then its ciphertext and bounded metadata enter the existing authenticated-state envelope. The global Activity and diagnostic paths never receive that text.

### Invariants

- Never disable, weaken, exclude, replace, or misrepresent the registered Windows antivirus provider.
- Never interpolate an attacker-controlled path into executable PowerShell.
- Never trust a path before full normalization, root containment where applicable, and reparse-point rejection.
- Never restore a quarantined payload before authenticating metadata and matching its stored SHA-256.
- Never convert an unknown executable or a clean AMSI result into publisher trust.
- Never accept an allow-list rule broader than an exact SHA-256 in the MVP.
- Never call an unavailable provider state healthy merely because Defender-specific status could not be queried.
- Never claim kernel, provider, ransomware, exploit, web, EDR, cloud-reputation, or certified antivirus capability from this user-mode companion.
- Never treat a peer ID as a command line, provide a remote password, configure unattended access, request elevation, install a remote-control service, hide consent UI, or claim that Soltex owns RustDesk session security.
- Never launch a selected remote client after its approved SHA-256 changes or its current Authenticode trust check fails.
- Never fetch update artifacts before the exact descriptor satisfies the active metadata-signature quorum, and never follow a redirect to an unsigned origin.
- Never treat downloaded/staged bytes as executable authority, cross an installer boundary from an unconfirmed plan, or expose arbitrary download-and-execute behavior.
- Never treat private-network membership, NAS possession, AI output, or a cloud login as authority to execute a device capability.
- Never expose a generic remote shell or silently move work Box data into personal Drive, the NAS, an AI prompt, or another trust domain.
- Never turn the Whisper shortcut hook into a keylogger: only configured-key state and content-free action transitions may leave the callback.
- Never accept an injected shortcut as dictation authority or retain raw keyboard/mouse events in logs, Activity, crash text, or evidence.
- Never read a password value, field value, selected text, caption, or surrounding text while inspecting a Whisper target.
- Never treat a timed-out, inaccessible, focus-changing, identity-less, or cross-integrity UI Automation provider as an editable target.
- Never upgrade a core copy decision to insertion, restore a clipboard after ownership changes, or treat an accepted `SendInput` sequence as proof that text arrived.

## Attack Surface, Mitigations, and Attacker Stories

### Defender process boundary

An attacker may choose a scan path containing quotes, metacharacters, or PowerShell syntax. `PowerShellDefenderClient` uses fixed encoded scripts and passes the normalized custom path through `SOLTEX_SCAN_PATH`; it is read as data. The child starts in its executable directory rather than an untrusted project/current directory. Output is redirected, byte-bounded while read, rejected on overflow, and parsed as JSON. Raw stderr is not copied into health or audit-facing models because it can echo the selected path; a bounded exit-code diagnostic is returned instead. A future regression that interpolates the path, restores post-read-only truncation, inherits an attacker-controlled working directory, or logs raw stderr would create command-execution, memory-exhaustion, or privacy risk.

Defender may be passive or disabled because another provider is active. Explicit WSC aggregate health remains authoritative: `Good` can represent that other provider, while `Poor`, `Snoozed`, and `NotMonitored` cannot be overridden by Defender detail flags. Only when WSC is unavailable may `Normal` plus active Defender antivirus/real-time flags provide a clearly disclosed fallback; passive Defender never fills that gap.

### Native library boundary

The security assembly imports only Windows system libraries for AMSI, WSC, Authenticode, DPAPI, and kernel memory release. An assembly-level `DefaultDllImportSearchPaths(System32)` policy prevents an application-directory or current-directory DLL from satisfying those imports. Removing that policy would reopen a native DLL preloading path before managed security checks run.

Defender orchestration launches the Windows PowerShell executable from System32 and does not rely on command-name module auto-loading. Each fixed script imports the Defender, Diagnostics, and Utility manifests through an explicit `$PSHOME\Modules\...\*.psd1` path and uses module-qualified cmdlets. A missing or invalid system manifest stops the operation instead of falling back to a same-named command from another module path. This reduces command-precedence and module-search hijacking risk; it does not independently authenticate Microsoft module contents or replace Windows file protection and Authenticode.

WSC callbacks can arrive during shutdown or in a burst. `ProtectionMonitor` treats them only as refresh signals, serializes observations, coalesces bursts, cancels each expired polling wait, preserves last-known-good state, retries nonfatal failures with bounded backoff, isolates provider/update-subscriber failures, and unregisters the callback on disposal. `WindowsSecurityChangeMonitor` invokes subscribers individually so one nonfatal handler failure cannot terminate the native notification path or suppress later handlers. The native shutdown-race and later fault-isolation regressions are included in the Windows-verified 27-case suite.

Defender Operational event XML can contain file, process, and scan-resource paths. The query is fixed to documented event IDs and bounded by lookback, result count, a 512 KiB captured-output ceiling, and command timeout. Excess child output is drained and discarded before rejection. Only the no-matching-events PowerShell condition becomes an empty success; provider, log, and access failures remain visible. The requested count is enforced again after JSON deserialization. The parser constructs details only from allow-listed non-path fields and marks resource values as redacted; it does not pass raw event messages or arbitrary XML fields into the UI.

### AMSI and file intake

An attacker may provide a large file, detection marker, script, malformed content, high-risk extension, or rapidly changing file. Soltex rejects reparse-point files, hashes streams, limits AMSI buffers to 16 MiB, and flags larger inputs for a Defender custom scan. Executable extensions remain `ReviewRecommended` after a clean AMSI result. File replacement between assessment and later consumption remains a time-of-check/time-of-use risk; production import code must consume the validated handle or revalidate immediately before use.

### Manifests and executable trust

An attacker may forge JSON, reuse a signature with modified content, replay/roll back/equivocate a trust policy, redirect acquisition, lie about lengths, truncate or expand a body, include duplicate/case-variant paths, escape the content root, swap a reparse point, or alter payload/source bytes during verification. Exact acquisition-descriptor bytes require RSA-PSS/SHA-256 metadata-signature quorum before network access. Trust rotation requires increasing sequence plus overlap with currently authorized keys/pins/publishers and rejects same-ID replacement. HTTPS requires an active TLS SPKI pin; redirects remain inside signed origins. Acquired bytes are length/hash bounded, held by exclusive handles, and cleaned on failure. The detached release-manifest signature covers exact bytes, archive staging is bounded, and publisher authorization uses an immutable private snapshot before the preview is built. Production policy still needs real approved identities, secure key custody, an authenticated release source, online revocation at an appropriate checkpoint, and an installer transaction.

An authenticated current/last-known-good planning journal records bounded sanitized phases and private staging tokens. A same-user attacker can delete both generations or deny access; Soltex fails closed and requires review. Cleanup accepts only authenticated Soltex token syntax and never creates installation state.

### Quarantine and allow list

An attacker may tamper with the quarantine index, replace a stored payload, force an overwrite on restore, or seed an overly broad exclusion. DPAPI protects the HMAC key; metadata is authenticated; payloads are re-hashed; restore refuses paths inside quarantine and chooses a timestamped sibling on collision. Cross-volume moves use copy, flush, hash verification, then source deletion. Allow decisions are capped and exact-hash only. A same-user attacker can still delete state, replace a DPAPI-wrapped key they can themselves unwrap, or race files; this MVP is not a protected service.

### Watcher, availability, and privacy

An attacker may flood the import folder. The channel holds at most 128 paths, drops the oldest under overload, debounces events, filters extensions, and has one sequential worker. Dropping means Soltex's extra assessment can be skipped under sustained flood; Windows real-time protection remains the primary control. Audit entries HMAC paths instead of storing them, bound text, chain entries, and rotate at 4 MiB. Local deletion/truncation remains possible and must not be represented as tamper-proof logging.

### WPF and user actions

Restore and permanent delete are user-driven high-impact actions with confirmation UI. The WPF process is unelevated and does not automate Windows Security settings. UI health labels must preserve unknown/provider-managed states and never turn missing telemetry into a green claim.

### Whisper global shortcut boundary

A malicious or buggy configuration could choose an operating-system chord, a broad
modifier-only binding, or one chord that is a prefix of another and thereby trigger
the wrong action while the user is still pressing keys. The core rejects Windows-key,
reserved, modifier-only, unsupported, duplicate, conflicting, and prefix-overlapping
bindings before any native registration begins. Default chords avoid those classes.

The native callback maps only supported virtual keys and Mouse 4/5, then immediately
forwards all input to the next hook. It retains physical-down and reference-counted
logical state only for keys used by the active set. Auto-repeat is ignored. Action
transitions enter a bounded channel; overflow disables the registration and surfaces
a content-free fault instead of leaving capture active. Candidate keyboard and mouse
hooks remain disabled until both install, so a partial replacement cannot destroy the
previous valid set. Injected events are measured but never dispatched as actions.

Low-level hooks remain an attractive same-user abuse surface. Same-user malware can
install its own hook, inject into this process, or manipulate the application, and an
administrator can bypass these controls. Future shipped integration must register
only after validated settings, cancel active work on hook faults, unregister during
the owned shutdown drain, and prove physical-key behavior without collecting user
content. Hook timing evidence contains only sample counts and percentile durations.

### Whisper provider credential boundary

Provider credentials are sensitive even when transcript and audio logging is off.
The provider-neutral model exposes only bounded content-free status: provider id,
display name, HTTPS endpoint, model identity, language and streaming capabilities,
privacy statement, and whether a credential is available. It never carries the
credential itself. Provider ids are restricted to a short ASCII identifier before
they can select a provider-scoped store name; endpoint metadata rejects non-HTTPS
addresses, embedded user information, and fragments.

The Windows credential adapter accepts bounded bytes directly and does not obtain
them from source, settings JSON, process arguments, or environment variables. It
protects one provider-scoped value with current-user DPAPI inside Soltex's
HMAC-authenticated state envelope. Clear managed and unmanaged working buffers are
zeroed at the end of their owned lifetime. A provider can acquire clear bytes only
through an owned lease; disposing that lease clears the same backing array observed
by callers. Rotation deletes the old current and backup generations before writing
the replacement, so a failed rotation prefers loss of availability over retaining an
old credential in Soltex's backup. Corrupted state fails closed but remains removable
through exact owned-artifact deletion.

This boundary does not protect a credential from malware already running as the same
user, an administrator, process injection, crash dumps, page files, storage snapshots,
or a compromised Windows cryptographic service. It does not prove any provider's
privacy properties and it does not configure, transmit, validate, or rotate an owner
credential at the remote provider. The future HTTP adapter must keep request bodies,
headers, response text, and exception details out of diagnostics and apply
`WhisperRedaction.Sanitize` before any provider failure crosses into content-free
evidence.

### Whisper focused-target boundary

A hostile, inaccessible, or hung UI Automation provider could block its caller,
change the focused element between reads, expose misleading editability metadata, or
attempt to make Soltex ingest private target content. The Windows adapter runs
provider calls off the UI thread under one total deadline and permits at most one
native inspection at a time. A timed-out worker is allowed to finish in the
background, but no second worker starts until it does; callers receive unknown rather
than hanging or accumulating threads. Focused process ID and opaque runtime ID are
re-read before acceptance, and a change invalidates the observation.

Inspection requests only content-free properties and pattern availability. Password
providers are not opened, and no code in this path asks UI Automation for Name,
Value, text, a selection, or surrounding context. Value/Text read-only and selection
support are queried only on non-password controls. Contextual formatting reads remain
disabled and require a future visible, per-application permission. The executable
remains `asInvoker` with `uiAccess=false`; elevated or protected UI is not automated,
and high/system/protected integrity resolves to the existing copy-only policy.

This boundary cannot defend against a malicious same-user process that lies through
its own accessibility provider, process injection, an administrator, or a compromised
Windows UI Automation subsystem. Identity checks and later insertion verification
must therefore be repeated immediately before insertion and submission. Current live
evidence covers controlled Win32 Edit plus WPF TextBox, RichTextBox, PasswordBox,
read-only, and focus-changing targets. WinUI, Chromium, Electron, Windows Terminal,
elevated applications, hostile third-party providers, and the rest of the application
matrix remain required before a shipped-support claim.

### Whisper insertion and clipboard boundary

Focus can move between target inspection, clipboard staging, paste dispatch, and
later submission. `WhisperInsertionPolicy` compares the captured and current atomic
snapshots before any action, and the Windows adapter repeats that comparison after
staging. A mismatch leaves the transcript copied and emits no paste input. Direct UI
Automation replacement is limited to an enabled, focusable, non-password, writable
ValuePattern control whose one TextPattern selection spans the whole document; no
target value or text is read to make that decision.

Clipboard contents are attacker-controlled and can change concurrently. Soltex
retains the prior OLE data object without enumerating or deserializing its formats,
sets only bounded Unicode text plus a random operation token, and records the
sequence number after rendering. Restore is optional and proceeds only when both the
sequence and token still match. A different sequence or token is ownership loss and
restore is skipped. Clipboard-open failures use bounded retries; cancellation before
paste attempts owned restoration, while a failed paste deliberately leaves the
transcript copied for recovery.

The paste path emits only Ctrl-down, V-down, V-up, Ctrl-up after confirming no Ctrl,
Shift, Alt, or Windows modifier is already down. It never emits transcript characters.
Injected shortcut events remain rejected by the shortcut adapter. `SendInput` is
subject to UIPI and may report only dispatch, not target receipt; therefore the
delivery result records `MutationDispatched`. The verified-submit adapter then
performs a bounded target-owned read-back and one final identity comparison before
asking the core gate for authorization. Same-user clipboard readers,
clipboard managers, malicious accessibility providers, and races inside Windows
between the final ownership check and OLE restoration remain outside complete
prevention and are addressed by fail-closed outcomes and later verification.

### Whisper verified-submission boundary

Enter is irreversible and can send a message, choose an application action, or
execute a terminal command. Submission therefore requires the original delivery
policy origin, recorded first-use consent, no cancellation, a matching final target,
and verified insertion. `WhisperSubmitGate` is the only component that grants the
authorization, and its permit can be consumed once. The Windows adapter emits only
Enter-down and Enter-up after consuming that permit. Modifier state, unavailable or
oversized read-back, read mismatch, inaccessible automation, provider timeout,
focus/category/editability/protection/integrity drift, and cancellation all produce
no Enter.

Read-back is sensitive even though it is not retained. It is deadline-bounded,
single-flight, limited to the focused control and 400,000 characters, rejected for
password targets, and passed directly to the verifier without diagnostics,
serialization, Activity, or evidence output. UI Automation may still allocate a
provider-returned Value string before Soltex can enforce its post-return length
limit; TextPattern reads use an explicit maximum. A same-user injector, malicious
provider, or focus change in the final gap between the last UIA comparison and
`SendInput` remains outside complete prevention. Soltex reduces that window, never
selects a recipient/channel/address/terminal, and keeps auto-send unavailable in the
shipped UI until the application matrix and coordinator lifecycle are proven.

### Whisper retained-history boundary

Transcript history is sensitive content. The default is bounded session memory;
disk retention requires an explicit Encrypted on this PC choice and a 1–90 day
period. At most 24 entries and 16,000 characters per entry cross the storage
boundary. Process names and fixed delivery categories remain bounded metadata;
transcript text is protected per record with current-user DPAPI before the document
enters Soltex's HMAC-authenticated, generation-numbered state envelope. Cleartext and
DPAPI working buffers are explicitly zeroed when their owned lifetime ends.

Load authenticates the document before parsing it, rejects excessive or malformed
records, decrypts only unexpired entries, and fails closed on authentication, DPAPI,
UTF-8, or schema failure. Expiry and per-entry deletion remove both the current and
last-known-good Whisper envelopes before committing surviving entries, so Soltex's
own backup does not preserve deliberately deleted text. Clear removes those two
exact Whisper artifacts and leaves the shared authenticated-state key intact for
other stores. A cancelled or failed transition cannot be reported as successful.

These controls do not provide forensic erasure. NTFS snapshots, backup software,
SSD wear levelling, page files, hibernation, crash dumps, and storage captured before
deletion may retain bytes outside Soltex's ownership. DPAPI and envelope
authentication also do not defend against malware already running as the same user,
an administrator, process injection, or a compromised Windows cryptographic service.
The UI therefore says encrypted retention and exact Soltex-owned deletion, never
secure erase or protection from a compromised account.

### External Remote Assist boundary

An attacker may persuade the user to select a renamed executable, replace an approved file, supply a peer ID containing shell syntax, or disguise an unauthorized support request. Soltex requires the exact filename `RustDesk.exe`, rejects a reparse-point file, fingerprints its bytes, asks Windows to verify Authenticode, and repeats byte/signature validation for every launch. Peer IDs are limited to 3–64 ASCII letters, numbers, hyphens, or underscores and enter `ProcessStartInfo.ArgumentList` as one value with `UseShellExecute=false`. Each launch requires local confirmation, and audit entries intentionally omit the peer ID.

These controls do not prove official RustDesk release provenance, remote-party identity, endpoint cleanliness, network security, or actual human consent on the other device. They also leave a narrow time-of-check/time-of-use interval before process creation. Soltex provides no password, unattended-access setting, elevation flag, service command, firewall mutation, retry loop, or background connection. A malicious administrator, same-user process injector, compromised Windows trust provider, or already-configured unattended RustDesk installation remains outside this adapter's enforcement. A production deployment needs publisher/release provenance, managed updates, compatibility testing, and a clearly owned support policy.

### Out-of-scope attacker stories

The current code cannot defend against a kernel attacker, compromised Windows trust provider, stolen release signing key, malicious administrator, same-user malware with unrestricted process injection, boot-time malware, direct raw-disk modification, or a supply-chain compromise before signing. These stories require platform, organizational, and release controls beyond this repository.

## Severity Calibration (Critical, High, Medium, Low)

### Critical

- A release/update path accepts attacker code without a valid approved signature and executes it for users.
- A future privileged service or driver exposes arbitrary kernel/system code execution.
- Soltex silently disables or weakens the registered antivirus provider across user systems.

### High

- Scan-path injection reaches PowerShell command execution as the user.
- Manifest traversal or archive extraction writes executable content outside its staging root.
- Quarantine restore can be redirected to overwrite startup or application binaries without authenticated metadata and explicit intent.
- A stolen signing key can publish accepted malicious updates without revocation/rollback controls.
- A remote-assistance path silently supplies credentials, bypasses consent, or launches attacker-selected code.

### Medium

- A race swaps a file after assessment but before import, causing unvalidated content to be consumed.
- Same-user state tampering produces false health, forged allow entries, or misleading audit data without code execution.
- Import flooding reliably bypasses Soltex assessment while the primary registered provider is unavailable.
- Raw paths or file contents leak into logs without consent.
- A trusted-but-unofficial signed executable is mistaken for an attested RustDesk release, or a user authorizes the wrong remote peer.

### Low

- A local unprivileged user causes bounded UI/service degradation with malformed but non-executed input.
- Audit rotation loses old non-security-critical history while current protection continues.
- Health wording is stale or confusing but does not alter protection or trust decisions.
- Developer-only render/test tooling fails without affecting release binaries.
