# Security policy

Soltex is pre-release software. Do not use public issues to disclose a vulnerability that could put users or their files at risk. Contact the repository owner privately and include the affected revision, reproducible steps using benign fixtures, impact, and suggested mitigations.

## Security invariants

- Soltex must never disable, weaken, exclude itself from, or misrepresent the antivirus provider registered with Windows.
- File paths, imported content, manifests, presets, plugins, archives, and update metadata are attacker-controlled until validated.
- PowerShell commands must be fixed application code; untrusted paths may cross the boundary only as data, never interpolated script.
- Quarantine restore must verify the stored hash and authenticated metadata before writing outside quarantine.
- Exact-hash allow-list entries require deliberate user action and must never become path-wide exclusions.
- Executable release artifacts and updates require both an approved publisher policy and authenticated content metadata.
- Security work must never execute on future real-time audio, capture, encoder, APO, or driver threads.
- Test material is limited to benign fixtures and the standard harmless EICAR marker. Do not add live malware to this repository.

The reusable repository threat model is maintained in `docs/THREAT_MODEL.md`.
