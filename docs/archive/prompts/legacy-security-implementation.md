# Security implementation continuation

Use this checklist for the next bounded implementation slice:

- [ ] Re-read `SECURITY.md`, `docs/THREAT_MODEL.md`, and `docs/IMPLEMENTATION_STATUS.md`.
- [ ] Preserve fixed PowerShell scripts and data-only path transfer.
- [ ] Create an approved-publisher policy with test and production identities separated.
- [ ] Add a manifest `releaseSequence`, persist the highest accepted sequence in authenticated state, and reject rollback.
- [ ] Bind staged content to both detached manifest and Authenticode publisher policy.
- [ ] Revalidate the exact file handle immediately before load/use.
- [ ] Add bounded archive extraction tests for traversal, duplicate case paths, links/reparse points, count, size, and ratio.
- [ ] Add fail-safe UI states for unavailable provider data and revoked/unknown publishers.
- [ ] Run Release build and all benign/EICAR tests.
- [ ] Recapture native WPF evidence and retain explicit nonclaims.
