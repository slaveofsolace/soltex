# Runtime lifecycle and Windows services

Snapshot: 2026-08-12

Status: exact-source owner-host accepted at `4207ecb70ef30c09203cb4f0f2b3efedf1ef2bd6`; published hosted source `6e54fb509ba332191107aa64733db0880e3cac78` accepted through Windows run `31568771869` and package run `31568771855`; owner visual acceptance pending

## User capability

The Applications workspace has three quiet, searchable views: Installed, Startup, and Services. Services shows display name, current state, start mode, and a narrow attention signal. It is observation only.

Settings also owns an explicit close behavior:

- **Exit** is the default and ends the Soltex desktop process;
- **Notification area** is opt-in, keeps the same desktop process open after its window closes, and exposes only Open Soltex and Exit Soltex commands;
- if the notification-area resource cannot be created, Soltex fails closed to Exit.

Soltex installs no background service. Performance sampling runs only while Home or Performance is visible in a non-minimized window. It stops when the window is hidden, minimized, or closing. Security Center change observation and the bounded Imports watcher remain active while the explicitly opted-in desktop process remains open.

The notification icon uses the documented Unicode `Shell_NotifyIconW` boundary directly and associates callbacks with Soltex's existing owned WPF window. It adds, versions, modifies, focuses, and removes one icon; re-adds it after the taskbar is recreated; and restores the main window if the icon becomes unavailable while hidden. This avoids carrying the complete Windows Forms runtime solely for one notification icon. The exact `4207ecb` package is 71,583,560 bytes, about 10.55 MiB smaller than the earlier Windows Forms comparison package, while preserving the tested show/hide/dispose lifecycle.

Accepted window close now has an explicit ownership boundary. Soltex cancels the active user operation and telemetry loop, waits up to 20 seconds for those tasks and startup to drain, and only then disposes the import monitor, protection monitor, update journal, and security runtime. A timeout skips disposal of resources that may still be referenced and lets process termination reclaim them; it does not race a live task against `QuarantineStore` disposal. Close-to-notification-area is still a cancelled close and does not enter shutdown.

Controlled render and runtime-probe modes use explicit WPF application shutdown. Render evidence waits up to 40 seconds for complete workspace initialization before capture, covering the composed bounded protection-health and event queries on a cold self-contained launch, then waits up to 25 seconds for resource cleanup after closing the window. Package CI independently caps the entire process at 80 seconds. Initialization, capture, abandoned cleanup, cleanup timeout, or outer process timeout produces a nonzero gate and retained diagnostics. The runtime-cost probe retains its stricter 20-second normal-startup bound. Exact-source regression evidence includes a zero-warning Release build, focused app tests, native states, and a packaged Security render.

## Read-only Service Control Manager boundary

`WindowsServiceInventoryProvider` uses the documented Windows Service Control Manager APIs with query-only access:

- `OpenSCManagerW` requests only `SC_MANAGER_ENUMERATE_SERVICE`;
- `EnumServicesStatusExW` requests Win32 services in all current states and excludes drivers;
- `OpenServiceW` requests only `SERVICE_QUERY_CONFIG`;
- `QueryServiceConfigW` reads only the start type used by the model;
- every SCM handle is closed through `CloseServiceHandle`.

The provider exposes at most 512 sorted rows from at most 2,048 observed entries. Enumeration is limited to the Windows-documented 256 KiB maximum buffer and each configuration query to 64 KiB. Labels are whitespace/control-character normalized, bounded, and replaced if path-like. Executable paths, service accounts, dependencies, descriptions, command lines, and raw configuration pointers never enter the public model. Inaccessible or omitted records produce Partial or Unavailable state; Soltex does not infer missing values.

The attention column is deliberately conservative. It reports a transition state, or Review only when an automatic stopped service also reports an unusual nonzero exit code. A stopped manual service is not labeled as a problem.

Soltex cannot start, stop, pause, enable, disable, reconfigure, delete, or install a service in this slice.

## Runtime-cost evidence contract

`eng/measure-runtime.ps1` launches the already-built WPF assembly through `dotnet exec`, tracks one process for at most 45 seconds, requires fresh output, and binds the JSON report to full source and tested commit identities. Schema 2 records:

- startup completion time;
- visible-idle, minimize-transition, minimized-steady, and hidden-notification-area samples;
- normalized whole-process CPU, working set, private memory, handles, and thread count;
- Performance-sampler state;
- dispatcher-thread and top-thread CPU attribution;
- 18 workspace transitions with mean and maximum elapsed time.

The minimize-transition sample is kept separate from minimized steady state. Soltex releases workspace animation clocks when they complete and clears remaining workspace animations before minimize. This avoids describing one-time transition work as continuous background cost while still retaining the transition measurement.

Exact-source owner-host evidence for `4207ecb70ef30c09203cb4f0f2b3efedf1ef2bd6`:

| Measure | Observed |
|---|---:|
| startup completion | 2,997.4 ms |
| 18 navigation transitions | 39.1 ms mean / 379.2 ms maximum |
| visible idle CPU | 0.643% normalized |
| minimize transition CPU | 2.531% normalized |
| minimized steady CPU | 0.000% normalized |
| hidden notification-area CPU | 0.000% normalized |

The one-time minimize transition was attributed to a worker/runtime thread, not the WPF dispatcher. That observation does not identify the worker implementation or prove the same timing on another machine. The exact report is retained outside the repository at:

```text
<local-evidence-root>\2026\08\12\soltex-product-rebuild\lifecycle-tray-exact-4207ecb-20260812-0055
```

Published hosted evidence independently bound source `6e54fb509ba332191107aa64733db0880e3cac78` to tested PR merge `ebd163553b3229099c371cd79b8967ace2b1ab55`, exactly one commit ahead with the source as merge base. All 15 retained 1280x820 captures matched their manifests. Hosted schema-2 runtime evidence recorded 1,040.5 ms startup, 19.2 ms mean / 150.8 ms maximum across 18 transitions, 4.617% visible-idle CPU, 21.472% minimize-transition CPU, and 0.000% minimized-steady/hidden CPU on the four-logical-processor runner. These values are not directly comparable to owner-host values or hardware benchmarks.

The downloaded hosted artifacts and selective pixel inspection are retained at:

```text
<local-evidence-root>\2026\08\12\soltex-product-rebuild\hosted-6e54fb5
```

## Nonclaims

This is not a service manager, driver inventory, startup optimizer, health diagnosis engine, historical performance database, independent benchmark, resident agent, or unattended correction system. Short samples are regression evidence, not cross-machine performance scores. Notification-area mode is not a Windows service and does not survive sign-out or reboot unless a separate future startup policy is explicitly designed and approved.

## Primary Windows references

- [EnumServicesStatusExW](https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-enumservicesstatusexw)
- [QueryServiceConfigW](https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-queryserviceconfigw)
- [QUERY_SERVICE_CONFIGW](https://learn.microsoft.com/en-us/windows/win32/api/winsvc/ns-winsvc-query_service_configw)
- [CloseServiceHandle](https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-closeservicehandle)
- [WPF application shutdown modes](https://learn.microsoft.com/en-us/dotnet/api/system.windows.application.shutdownmode)
- [Shell_NotifyIconW](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw)
- [NOTIFYICONDATA](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-notifyicondataw)
