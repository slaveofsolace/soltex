# Native instrument design references

Acquired: 2026-08-25 12:17 America/Chicago

Disposition: **REFERENCE ONLY**

## Apple Human Interface Guidelines

- Source: Apple Inc.; <https://developer.apple.com/design/human-interface-guidelines/>.
- Supporting pages:
  <https://developer.apple.com/design/human-interface-guidelines/design-principles>,
  <https://developer.apple.com/design/human-interface-guidelines/typography>, and
  <https://developer.apple.com/design/human-interface-guidelines/layout>.
- Version: live public documentation as inspected on the acquisition date; no
  source revision identifier was published on the inspected pages.
- Rights/entitlement: public documentation viewed for general design guidance.
  No entitlement to reuse Apple design resources, assets, fonts, code, or trade
  dress is asserted.
- Acquisition and archive scan: web documentation was inspected in place. No
  archive, installer, binary, design resource, or payload was downloaded, so an
  archive malware scan and payload SHA-256 are not applicable.
- Inspection: retained only general principles of purpose, agency,
  responsibility, familiarity, flexibility, simplicity, craft, delight,
  hierarchy, readable typography, alignment, and adaptive layout.
- Clean-room adaptation: Soltex independently translates these principles into
  one dominant task per workspace, calm spacing, concise copy, progressive
  disclosure, clear feedback, and recoverable actions. It does not reproduce
  Apple component geometry, materials, platform layouts, wording, or visual
  identity.
- Runtime proof: the onboarding renders referenced by
  `SHELL_ONBOARDING_SLICE.md` prove the current independent hierarchy and
  compact-shell behavior only. They do not prove Apple parity or human
  acceptance.
- Release ledger: admitted as **REFERENCE ONLY**; no redistributable item enters
  the package or SBOM.

## Microsoft Mica guidance

- Source: Microsoft; <https://learn.microsoft.com/windows/apps/design/style/mica>.
- Version: live Windows application design documentation inspected on the
  acquisition date.
- Rights/entitlement: public product documentation used as the authoritative
  behavior reference for a future Windows-native backdrop.
- Acquisition and archive scan: inspected in place; no payload was downloaded,
  so archive scan and payload SHA-256 are not applicable.
- Inspection: Mica is intended as the base layer of a long-lived app window,
  with a restrained content layer and automatic solid/high-contrast fallbacks.
- Adaptation: the next native-chrome slice may use supported Windows APIs for
  the shell base layer while retaining Soltex's own tokens and identity.
- Runtime proof: current controlled runtime sidecars record the DWM Mica success
  path after AppWindow attachment. The off-screen client bitmap does not prove
  visible translucency or native frame interaction, which remain separate
  physical gates.
- Release ledger: documentation-only input; no external runtime or asset has
  been admitted.

## Windows App SDK windowing component candidate

- Aggregate source: Microsoft;
  <https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.4.0>.
- Selected component source: Microsoft;
  <https://www.nuget.org/packages/Microsoft.WindowsAppSDK.InteractiveExperiences/2.1.6>.
- Versions: Windows App SDK aggregate 2.4.0, Interactive Experiences 2.1.6,
  and its declared Base dependency 2.0.4; checked 2026-08-25.
- License: Microsoft Windows App SDK software license terms. The package terms
  permit redistribution of files binplaced with an application subject to
  their distribution requirements. The package license and notices remain in
  the NuGet cache and must be represented in the final release ledger.
- Entitlement: public Microsoft NuGet packages; no account, payment, trial, or
  authenticated entitlement action was required.
- Quarantine root:
  `D:\SoltexTaskCache\quarantine\windowsappsdk-2.4.0`.
- Aggregate archive SHA-256:
  `6ec2ebb6add33ecebac1f5773ad4cabe934b82fb18d7bea98e011bb0fc0a37b9`.
- Interactive Experiences archive SHA-256:
  `de7b5907c63c8a79606ccc8f0d98943b154a2e62312308187e8cdc3304ff3d0b`.
- Base archive SHA-256:
  `e3e13478c4c80c59ed5f8f89542fe49a2985daa484753e93a5858e90c2d46a4d`.
- Package verification: all three archives passed NuGet author and repository
  signature verification. Non-extracting inventories found no traversal,
  symlink, encryption, duplicate-path, or nested-archive issue. The selected
  component contains managed projections and native windowing DLLs, so it
  correctly remains `review-required` until build and runtime proof.
- MSBuild inspection: the direct component imports Microsoft-authored props and
  targets, selects only the current architecture in self-contained mode, copies
  the component payload, and generates the required WinRT manifest. These are
  build-time active inputs and are not treated as malware clearance.
- Scope reduction: the broad aggregate would also admit WinUI, AI, ML, Search,
  Widgets, DWrite, Foundation, and Runtime components that this slice does not
  use. It is therefore not referenced. The direct Interactive Experiences
  component plus its Base dependency is the bounded candidate.
- Adaptation: use AppWindow only for native window/title-bar integration. Mica
  remains on the documented DWM path; no WinUI control or copied sample code is
  admitted.
- Runtime proof: the isolated adapter and self-contained WPF host build with
  zero warnings and zero errors. Launched client renders exit cleanly and their
  content-free sidecars report successful AppWindow attachment and DWM Mica on
  the owner-controlled Windows 11 host. An extracted executable manifest
  contains Microsoft UI Windowing activation declarations, and the measured x64
  windowing payload is 17,881,280 bytes. The client capture does not include the
  native non-client frame, so caption controls and snap-layout interaction still
  require direct physical observation.
- Release ledger: **ADAPT**, not release-admitted. Package-size, runtime,
  accessibility, shutdown, MSIX, notice, SBOM, and human acceptance gates remain
  open.

## Boundary

The Apple reference defines the desired emotional qualities; Microsoft guidance
defines the Windows platform behavior. Soltex's code, layout, copy, tokens, and
interaction design remain independently authored.
