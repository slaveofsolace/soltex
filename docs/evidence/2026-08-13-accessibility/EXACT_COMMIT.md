# Accessibility contract exact-commit evidence

## Identity

- Source commit: `06f6a3b7461e813119b3f581aed69a6d2c3cf97e`
- Tested pull-request merge: `d730a8e030bda8fbd43a403495e7e991303f0f27`
- GitHub comparison: the tested merge is exactly one commit ahead of the source,
  uses the source as its merge base, and contains no file diff from the source.
- Branch: `sol/soltex-product-rebuild`
- Pull request: [#11](https://github.com/slaveofsolace/soltex/pull/11),
  intentionally draft.

## Implemented contract

- 86 interactive controls across 11 shipping XAML files have a programmatic
  name or fixed visible button label.
- The modal command palette cycles Tab and Control+Tab within its surface.
- Empty command results and Remote Assist session state use polite live-region
  announcements.
- Workspace motion follows the Windows client-area animation preference.
- `eng/verify-accessibility-contracts.ps1` enforces these source contracts in
  both the canonical verifier and the hosted Windows workflow.

## Owner-host verification

- Canonical transcript: `canonical.log`
- Transcript SHA-256:
  `D3022AF0AF16A3B53C9A964CFC4996BAC75611D66008978A39B8B3BC6D7E553F`
- Identity policy: 224 tracked text files with 9 reasoned allowlist entries.
- Design-token policy: 10 XAML files.
- Accessibility contract: 86/86 controls across 11 XAML files.
- Release build: 0 warnings and 0 errors.
- Required suites: Security 32/32, supply chain 18/18, hardening 12/12,
  Updates 17/17, Device Fabric 24/24, Monitoring 16/16, Audio 21/21,
  Benchmarks 6/6, and App/control 34/34 — 180/180 total.
- Exact-commit native matrix: 18/18 non-empty 1280×820 WPF captures.
- Local render manifest SHA-256:
  `6B139FB92AD343CF41FB1713802636DAD52DFC97DE293CB9EF6A2AF3B6602411`.
- Every retained local PNG matched its recorded manifest length and SHA-256.
- Command Palette, Activity, and Remote Assist were directly inspected with no
  clipping, exception surface, or state ambiguity observed.

Local render evidence is retained outside the repository at:

- `<local-evidence-root>\2026\08\13\soltex-product-rebuild\06f6a3b-native`
- `<local-evidence-root>\2026\08\13\soltex-product-rebuild\06f6a3b-native-validation`

## Hosted verification

- [Windows run 31709142534](https://github.com/slaveofsolace/soltex/actions/runs/31709142534):
  success.
- [Package-smoke run 31709142535](https://github.com/slaveofsolace/soltex/actions/runs/31709142535):
  success.
- Windows artifact `9184583480` digest:
  `sha256:0cc6b698675feaea1a4de688491fa663cc09571e9b60fe8b65f4f8df2172b47c`.
- Package artifact `9184504652` digest:
  `sha256:fce90e052a7fbd7ec3f10f7699d409dc655f612df55e52fe0da124299055c54c`.
- Both downloaded ZIPs independently matched those GitHub digests.
- Hosted render manifest SHA-256:
  `D8A5B1003518E8205CD8AB107788CD44CF9DB71C8EE497E938F582CB35CDE7AD`.
- All 18 hosted PNG identities independently matched the manifest.
- Hosted Command Palette, Activity, and packaged Home were directly inspected
  with no clipping, exception surface, or state ambiguity observed.

Hosted runtime measurements are short regression samples, not a benchmark:

- startup: 1038.6227 ms;
- 18 navigation transitions: 18.8709 ms mean, 202.1681 ms maximum;
- visible idle: 5.8547% processor-normalized CPU;
- minimized steady: 0%;
- hidden notification-area state: 0%;
- Performance sampling was inactive while minimized and hidden.

The hosted self-contained `win-x64` package produced:

- `Soltex.exe`: 71,643,736 bytes, SHA-256
  `4485C2CEC97ED4BC80C3F60C4F7C92680845D5C9B43CF4E596DCC74B7BFD9556`,
  Authenticode status `NotSigned`;
- packaged Home: 90,508 bytes, SHA-256
  `64FC319E78CCB0ABCDEA555F22411018D1701A31850701CB1CE3E311984930D6`.

## Boundaries and nonclaims

- The hosted required gate passed. Optional hosted AMSI/EICAR remained 31/32
  because the runner's installed provider returned native result `1`. This is
  provider-interoperability evidence, not an antivirus-efficacy verdict.
- Static/source enforcement and native render inspection do not prove live
  keyboard focus order, Windows UI Automation behavior, screen-reader quality,
  high-contrast behavior, or 100/125/150/200% scaling.
- No WCAG or accessibility-conformance claim is made.
- Owner visual acceptance remains open; the pull request therefore remains
  draft.
- The package remains unsigned, and installer upgrade, repair, rollback, and
  uninstall lifecycle acceptance remain open.
- Defensive benchmark filesystem hardening is applied. Real-world
  exploitability was not dynamically established because policy-safe validation
  was intentionally not attempted.
