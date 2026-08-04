# Soltex documentation map

Current continuation begins with [`../HANDOFF.md`](../HANDOFF.md).

- `ARCHITECTURE.md`: component and trust-boundary map.
- `IMPLEMENTATION_STATUS.md`: evidence-backed feature status and remaining work.
- `IMMERSIVE_WORKSPACE.md`: current visual system, telemetry provenance, interaction states, and UI nonclaims.
- `REFERENCE_SYSTEMS.md`: clean-room AppControl/Zen/CAM/Sonar/RustDesk/Tailscale/security/cloud lessons and staged product requirements.
- `GITHUB_CONSOLIDATION.md`: audited linear branch stack, single-integration-PR contract, evidence fields, and post-merge cleanup proof.
- `MASTER_PROJECT_BLUEPRINT.md`: product direction and staged roadmap.
- `NAMING_AND_COMPATIBILITY.md`: identity migration and installed-state contract.
- `VALIDATION.md`: exact build, suite, EICAR, render, and evidence procedure.
- `THREAT_MODEL.md`: assets, adversaries, abuse cases, and mitigations.
- `SUPPLY_CHAIN_SECURITY.md`: publisher, release, transport, and update boundaries.
- `SECURITY_ENGINEERING_HANDOFF.md`: supported Windows security interfaces and nonclaims.
- `REMOTE_ASSIST.md`: RustDesk clean-room external-client boundary.
- `PERSONAL_DEVICE_FABRIC.md`: typed multi-device architecture and connector separation.
- `PRODUCT_SPEC.md` and `PARITY_MATRIX.md`: product requirements and demonstrated parity.
- `RESEARCH_AUDIT.md`: factual source and clean-room research record.

`evidence/` is append-only run evidence. `archive/prompts/` preserves superseded planning prompts for provenance; those files are not current instructions and are intentionally excluded from product-identity enforcement only through a documented historical allowlist.

The current immersive-workspace evidence packet is `evidence/2026-08-04-immersive-workspace/`; it freezes the Windows gate, screenshot hashes, Human Eye review, Resource Pilfer disposition, and Human Cortex decision ledger for implementation commit `2a0699b2ca77b30fa636279b1d5ecab603a8bde9`.

The bounded public-reference research packet is `evidence/2026-08-04-reference-systems/`. It records why the proprietary AppControl desktop installer was not acquired and retains a fail-closed Resource Pilfer candidate registry.
