# Activity privacy and retention

Snapshot: 2026-08-11

Status: implemented in the draft rebuild; exact-head hosted/package verification and owner visual acceptance remain open

## Purpose

Activity answers a narrow question: what meaningful action or recovery transition occurred inside Soltex? It is not a surveillance log, browser history, Windows event-log replacement, security audit ledger, or historical telemetry database.

Examples admitted by the current integration include explicit process-action results, Defender request/result transitions, quarantine changes, Remote Assist launch results, import-monitor transitions, and telemetry failure/recovery. Routine clicks, searches, browsing, packet contents, command lines, executable paths, and raw file paths are not recorded. Defender's bounded path-redacted event list and the authenticated security audit log remain separate surfaces with different trust and retention contracts.

## Storage contract

- The default is **Session only**. Entries live in memory and no `activity.json` file is created.
- **7 days** and **30 days** are explicit Settings choices on the current Windows account.
- The in-memory and retained timeline holds at most 120 newest entries.
- A retained document may not exceed 256 KiB.
- Each entry contains only a UTC timestamp, a sanitized area of at most 32 characters, and a sanitized summary of at most 220 characters.
- Control characters and excess whitespace are removed. Drive-root and UNC-like text in either untrusted display field is replaced with a generic area/local-item description.
- Retained JSON uses a versioned, unknown-member-rejecting schema with bounded deserialization depth.
- Writes use a same-directory temporary file and atomic replacement. Storage errors keep the bounded current-session view available and visibly change the retention state to `CHECK`; they are not reported as successful persistence.

The normal canonical path is `%LocalAppData%\Soltex\activity.json`. A profile that already owns the sole compatible legacy product-data root continues using that root; Soltex does not merge or silently move security state.

## Recovery and deletion

Loading retained history rejects unsupported schema data, invalid JSON, oversized bytes, missing required values, timestamps more than five minutes in the future, expired entries, and overflow entries. Invalid documents recover to an explicitly empty timeline. Expired, future, or unsupported individual entries are hidden; Soltex attempts to rewrite only the remaining bounded entries and reports cleanup failure if replacement cannot complete.

Changing from 30 days to 7 days, or from retained history to Session only, requires main-window confirmation. A confirmed switch to Session only attempts to remove the retained file while preserving current-session entries in memory. `Clear activity` separately requires confirmation, clears the visible timeline, and deletes the retained file. A deletion failure is surfaced as `CHECK` with truthful copy; the UI does not claim the saved bytes were removed.

## Nonclaims

Activity is not tamper-evident, append-only, encrypted, synchronized, exported to a NAS, exposed to an AI client, or suitable for compliance/security forensics. It does not yet retain numeric performance history. Those capabilities require separate threat models, migration/versioning rules, measured idle/storage cost, user-controlled export, and independent evidence.
