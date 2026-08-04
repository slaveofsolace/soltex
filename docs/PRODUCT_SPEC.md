# Product specification

## Product intent

Soltex is a lightweight Windows control surface for audio routing, local game clips, and device security visibility. The experience should feel like one coherent utility while each subsystem stays isolated enough that capture or security work cannot disturb real-time audio.

## Current release slice

The current executable implements the Security companion. Audio and Clips appear in navigation to preserve the product structure, but their native DSP, capture, encoding, and driver pipelines are not present in this reconstructed workspace.

The Security slice must:

1. show whether Windows reports a healthy antivirus provider;
2. show detailed Defender layers when Defender is available;
3. launch supported Defender operations without command injection;
4. inspect content at Soltex's import boundary with AMSI;
5. verify hashes, detached manifests, and Authenticode signatures;
6. isolate high-confidence detections in a recoverable quarantine;
7. keep allow-list scope to exact hashes;
8. retain a local, bounded, privacy-preserving audit trail;
9. remain usable without elevation for normal health and intake operations;
10. label unavailable or provider-managed data honestly.

## Product principles

- **Cooperate with Windows.** The registered provider owns system-wide real-time protection and remediation.
- **No duplicate resident engine.** Soltex watches only its import folder and explicitly selected targets.
- **Fail safely.** Invalid signatures, traversal, reparse points, state tampering, and ambiguous executable content cannot silently become trusted.
- **Recover first.** Quarantine is reversible until the user explicitly deletes an item.
- **No comparative claims.** Passing EICAR proves integration, not a detection rate.
- **Clean-room compatibility.** Public behavior can inform requirements; proprietary code, data, visual identity, signatures, and protocols cannot be copied.
