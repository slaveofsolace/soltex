[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InitialInstallerPath,
    [Parameter(Mandatory = $true)]
    [string]$UpgradeInstallerPath,
    [string]$OutputDirectory = 'artifacts\validation\installer',
    [Parameter(Mandatory = $true)]
    [string]$SourceHeadSha,
    [Parameter(Mandatory = $true)]
    [string]$TestedCommitSha,
    [switch]$EphemeralHostConfirmed
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-RepositoryPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Assert-CommitSha {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Value
    )

    if ($Value -notmatch '^[0-9a-fA-F]{40}$') {
        throw "$Name must be a complete 40-character Git commit SHA."
    }
}

function Invoke-OwnedProcess {
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$LogPath,
        [ValidateRange(10, 300)][int]$TimeoutSeconds = 120
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        if (-not $process.Start()) {
            throw "The owned '$Id' process could not start."
        }

        $ownedPid = $process.Id
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            try {
                $process.Kill($true)
                $process.WaitForExit(5000) | Out-Null
            }
            catch {
                # The exact owned process may have exited during timeout handling.
            }

            throw "The owned '$Id' process exceeded $TimeoutSeconds seconds; only PID $ownedPid and its child tree were stopped."
        }

        $process.WaitForExit()
        $stopwatch.Stop()
        $standardOutput = $stdoutTask.GetAwaiter().GetResult()
        $standardError = $stderrTask.GetAwaiter().GetResult()
        @(
            "operation=$Id"
            "owned_pid=$ownedPid"
            "exit_code=$($process.ExitCode)"
            "duration_ms=$($stopwatch.Elapsed.TotalMilliseconds.ToString('F1', [System.Globalization.CultureInfo]::InvariantCulture))"
            'stdout:'
            $standardOutput
            'stderr:'
            $standardError
        ) | Set-Content -Encoding UTF8 -LiteralPath $LogPath
        if ($process.ExitCode -ne 0) {
            throw "The owned '$Id' process failed with exit code $($process.ExitCode). See '$LogPath'."
        }

        return [ordered]@{
            owned_pid = $ownedPid
            duration_ms = [math]::Round($stopwatch.Elapsed.TotalMilliseconds, 1)
            exit_code = $process.ExitCode
        }
    }
    finally {
        $process.Dispose()
    }
}

if (-not $EphemeralHostConfirmed -or
    -not [string]::Equals($env:GITHUB_ACTIONS, 'true', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Installer lifecycle verification is destructive to a fresh test profile and runs only on an explicitly confirmed ephemeral GitHub Actions host.'
}

Assert-CommitSha -Name 'SourceHeadSha' -Value $SourceHeadSha
Assert-CommitSha -Name 'TestedCommitSha' -Value $TestedCommitSha
$SourceHeadSha = $SourceHeadSha.ToLowerInvariant()
$TestedCommitSha = $TestedCommitSha.ToLowerInvariant()
$checkedOutCommit = (& git -C $repoRoot rev-parse HEAD).Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $checkedOutCommit -ne $TestedCommitSha) {
    throw 'The installer lifecycle checkout does not match TestedCommitSha.'
}

$initialInstaller = Resolve-RepositoryPath -Path $InitialInstallerPath
$upgradeInstaller = Resolve-RepositoryPath -Path $UpgradeInstallerPath
$outputRoot = Resolve-RepositoryPath -Path $OutputDirectory
$reportPath = Join-Path $outputRoot 'installer-lifecycle.json'
$cleanupReportPath = Join-Path $outputRoot 'whisper-uninstall-cleanup.json'
$installLog = Join-Path $outputRoot 'install-initial.log'
$upgradeLog = Join-Path $outputRoot 'install-upgrade.log'
$uninstallLog = Join-Path $outputRoot 'uninstall.log'
$innoInstallLog = Join-Path $outputRoot 'inno-install-initial.log'
$innoUpgradeLog = Join-Path $outputRoot 'inno-install-upgrade.log'
$innoUninstallLog = Join-Path $outputRoot 'inno-uninstall.log'
foreach ($path in @($initialInstaller, $upgradeInstaller)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Installer was not found: '$path'."
    }
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
foreach ($reservedPath in @(
        $reportPath,
        $cleanupReportPath,
        $cleanupReportPath + '.error.txt',
        $installLog,
        $upgradeLog,
        $uninstallLog,
        $innoInstallLog,
        $innoUpgradeLog,
        $innoUninstallLog)) {
    if (Test-Path -LiteralPath $reservedPath) {
        throw "Refusing to reuse installer lifecycle evidence: '$reservedPath'."
    }
}

$installRoot = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\Soltex'))
$productRoot = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Soltex'))
$uninstallRegistryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{8E1F0C6A-5B3D-4A77-9F2E-1C4D6B8A0E52}_is1'
if ((Test-Path -LiteralPath $installRoot) -or
    (Test-Path -LiteralPath $productRoot) -or
    (Test-Path -LiteralPath $uninstallRegistryPath)) {
    throw 'The ephemeral runner is not fresh: an installed app or Soltex profile already exists.'
}

$expectedRuntimeFiles = @(
    'ggml-base-whisper.dll',
    'ggml-cpu-whisper.dll',
    'ggml-whisper.dll',
    'whisper.dll'
)
$modelFileName = 'ggml-large-v3-turbo-q5_0.bin'
$previousCleanupReport = $env:SOLTEX_WHISPER_UNINSTALL_REPORT_PATH
$installed = $false
try {
    $initialProcess = Invoke-OwnedProcess `
        -Id 'install-initial' `
        -FilePath $initialInstaller `
        -LogPath $installLog `
        -Arguments @(
            '/VERYSILENT',
            '/SUPPRESSMSGBOXES',
            '/NORESTART',
            '/NOICONS',
            "/LOG=$innoInstallLog")
    $installed = $true

    $installedExe = Join-Path $installRoot 'Soltex.exe'
    $runtimeRoot = Join-Path $installRoot 'runtimes\win-x64'
    if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf)) {
        throw 'The initial installer did not install Soltex.exe.'
    }
    $actualRuntimeFiles = @(
        Get-ChildItem -LiteralPath $runtimeRoot -File |
            Select-Object -ExpandProperty Name |
            Sort-Object)
    if ((Compare-Object $expectedRuntimeFiles $actualRuntimeFiles).Count -ne 0) {
        throw 'The initial installer did not install exactly the four Whisper CPU runtime DLLs.'
    }

    $modelsRoot = Join-Path $productRoot 'whisper\models'
    $stateRoot = Join-Path $productRoot 'state'
    New-Item -ItemType Directory -Force -Path $modelsRoot,$stateRoot | Out-Null
    $modelPath = Join-Path $modelsRoot $modelFileName
    $ownerNotePath = Join-Path $modelsRoot 'owner-note.txt'
    $settingsPath = Join-Path $productRoot 'whisper-settings.json'
    $settingsTemporaryPath = $settingsPath + '.tmp-' + [Guid]::NewGuid().ToString('N')
    $sharedKeyPath = Join-Path $stateRoot 'state.key'
    [System.IO.File]::WriteAllBytes($modelPath, [byte[]](1, 2, 3))
    [System.IO.File]::WriteAllText($ownerNotePath, 'preserve')
    [System.IO.File]::WriteAllText($settingsPath, '{}')
    [System.IO.File]::WriteAllText($settingsTemporaryPath, 'temporary')
    [System.IO.File]::WriteAllBytes($sharedKeyPath, [byte[]](7, 8, 9))

    $upgradeProcess = Invoke-OwnedProcess `
        -Id 'install-upgrade' `
        -FilePath $upgradeInstaller `
        -LogPath $upgradeLog `
        -Arguments @(
            '/VERYSILENT',
            '/SUPPRESSMSGBOXES',
            '/NORESTART',
            '/NOICONS',
            "/LOG=$innoUpgradeLog")
    if (-not (Test-Path -LiteralPath $modelPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $settingsPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $ownerNotePath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $sharedKeyPath -PathType Leaf)) {
        throw 'The upgrade did not preserve the pre-existing model/settings/shared-state fixture.'
    }

    $registration = Get-ItemProperty -LiteralPath $uninstallRegistryPath
    if ($registration.DisplayVersion -ne '0.0.2') {
        throw "The upgrade registration reports '$($registration.DisplayVersion)' instead of 0.0.2."
    }

    $uninstaller = Join-Path $installRoot 'unins000.exe'
    if (-not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
        throw 'The installed uninstaller was not found.'
    }
    $env:SOLTEX_WHISPER_UNINSTALL_REPORT_PATH = $cleanupReportPath
    $uninstallProcess = Invoke-OwnedProcess `
        -Id 'uninstall' `
        -FilePath $uninstaller `
        -LogPath $uninstallLog `
        -Arguments @(
            '/VERYSILENT',
            '/SUPPRESSMSGBOXES',
            '/NORESTART',
            "/LOG=$innoUninstallLog")
    $installed = $false

    if ((Test-Path -LiteralPath $installRoot) -or
        (Test-Path -LiteralPath $uninstallRegistryPath)) {
        throw 'Clean uninstall left installed program or registration state behind.'
    }
    if ((Test-Path -LiteralPath $modelPath) -or
        (Test-Path -LiteralPath $settingsPath) -or
        (Test-Path -LiteralPath $settingsTemporaryPath)) {
        throw 'Clean uninstall retained an exact-owned Whisper model or settings artifact.'
    }
    if (-not (Test-Path -LiteralPath $ownerNotePath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $sharedKeyPath -PathType Leaf)) {
        throw 'Clean uninstall removed unrelated model data or the shared authenticated-state key.'
    }
    if (-not (Test-Path -LiteralPath $cleanupReportPath -PathType Leaf)) {
        throw 'Clean uninstall did not produce its content-free cleanup report.'
    }
    $cleanup = Get-Content -Raw -LiteralPath $cleanupReportPath | ConvertFrom-Json
    if ($cleanup.schemaVersion -ne 1 -or
        $cleanup.outcome -ne 'cleaned' -or
        $cleanup.modelArtifactRemoved -ne $true -or
        $cleanup.settingsRemoved -ne $true -or
        $cleanup.settingsTemporaryArtifactsRemoved -ne 1 -or
        $cleanup.contentCaptured -ne $false) {
        throw 'The Whisper uninstall cleanup report failed its content-free contract.'
    }

    $runtimeHashes = [ordered]@{}
    foreach ($name in $expectedRuntimeFiles) {
        $publishedRuntime = Join-Path $repoRoot "artifacts\publish\win-x64\runtimes\win-x64\$name"
        $runtimeHashes[$name] = (Get-FileHash -LiteralPath $publishedRuntime -Algorithm SHA256).Hash.ToLowerInvariant()
    }

    [ordered]@{
        schema_version = 1
        source_head_sha = $SourceHeadSha
        tested_commit_sha = $TestedCommitSha
        host = 'ephemeral-github-actions-windows'
        initial_installer = [ordered]@{
            version = '0.0.1'
            bytes = (Get-Item -LiteralPath $initialInstaller).Length
            sha256 = (Get-FileHash -LiteralPath $initialInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
            process = $initialProcess
        }
        upgrade_installer = [ordered]@{
            version = '0.0.2'
            bytes = (Get-Item -LiteralPath $upgradeInstaller).Length
            sha256 = (Get-FileHash -LiteralPath $upgradeInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
            process = $upgradeProcess
        }
        installed_runtime_sha256 = $runtimeHashes
        model_retained_on_upgrade = $true
        settings_retained_on_upgrade = $true
        clean_uninstall = [ordered]@{
            installed_files_removed = $true
            exact_model_removed = $true
            exact_settings_removed = $true
            unrelated_model_file_preserved = $true
            shared_state_key_preserved = $true
            content_captured = $false
            process = $uninstallProcess
        }
        recorded_at_utc = (Get-Date).ToUniversalTime().ToString('o')
    } | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 -LiteralPath $reportPath

    Write-Host 'Installer install, upgrade, runtime payload, and clean-uninstall evidence passed.'
    Write-Host "Report: $reportPath"
}
finally {
    $env:SOLTEX_WHISPER_UNINSTALL_REPORT_PATH = $previousCleanupReport
    if ($installed) {
        $uninstaller = Join-Path $installRoot 'unins000.exe'
        if (Test-Path -LiteralPath $uninstaller -PathType Leaf) {
            try {
                & $uninstaller /VERYSILENT /SUPPRESSMSGBOXES /NORESTART | Out-Null
            }
            catch {
            }
        }
    }

    if (Test-Path -LiteralPath $productRoot) {
        $expectedProductRoot = [System.IO.Path]::GetFullPath(
            (Join-Path $env:LOCALAPPDATA 'Soltex'))
        if (-not [string]::Equals(
                $productRoot,
                $expectedProductRoot,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to clean an unexpected product-data path.'
        }

        Remove-Item -LiteralPath $productRoot -Recurse -Force
    }
}
