[CmdletBinding()]
param(
    [string]$OutputDirectory = 'artifacts\visual\whisper-stage6',
    [string]$ValidationDirectory = 'artifacts\validation\whisper-stage6',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$SourceHeadSha,
    [string]$TestedCommitSha,
    [string]$DotnetPath,
    [ValidateRange(5, 60)]
    [int]$RenderTimeoutSeconds = 20,
    [switch]$AllowDirtyWorkingTree
)

$ErrorActionPreference = 'Stop'
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
        throw "$Name must be a complete 40-character Git commit SHA; received '$Value'."
    }
}

function Invoke-OwnedRender {
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $resolvedDotnet
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
            throw "Render '$Id' could not start."
        }

        $ownedPid = $process.Id
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($RenderTimeoutSeconds * 1000)) {
            try {
                $process.Kill($true)
                $process.WaitForExit(5000) | Out-Null
            }
            catch {
                # The exact owned process may have exited between the timeout and stop request.
            }

            $stopwatch.Stop()
            @(
                "render_id=$Id"
                "owned_pid=$ownedPid"
                'outcome=timeout'
                "timeout_seconds=$RenderTimeoutSeconds"
                "duration_ms=$($stopwatch.Elapsed.TotalMilliseconds.ToString('F1', [System.Globalization.CultureInfo]::InvariantCulture))"
            ) | Set-Content -Encoding UTF8 -LiteralPath $LogPath
            throw "Render '$Id' exceeded $RenderTimeoutSeconds seconds. Only owned PID $ownedPid and its child tree were stopped."
        }

        $process.WaitForExit()
        $stopwatch.Stop()
        $standardOutput = $stdoutTask.GetAwaiter().GetResult()
        $standardError = $stderrTask.GetAwaiter().GetResult()
        $exitCode = $process.ExitCode
        @(
            "render_id=$Id"
            "owned_pid=$ownedPid"
            "exit_code=$exitCode"
            "duration_ms=$($stopwatch.Elapsed.TotalMilliseconds.ToString('F1', [System.Globalization.CultureInfo]::InvariantCulture))"
            'stdout:'
            $standardOutput
            'stderr:'
            $standardError
        ) | Set-Content -Encoding UTF8 -LiteralPath $LogPath

        if ($exitCode -ne 0) {
            throw "Render '$Id' failed with exit code $exitCode. See '$LogPath'."
        }

        return [ordered]@{
            owned_pid = $ownedPid
            exit_code = $exitCode
            duration_ms = [math]::Round($stopwatch.Elapsed.TotalMilliseconds, 1)
        }
    }
    finally {
        $process.Dispose()
    }
}

function Read-PngDimensions {
    param([Parameter(Mandatory = $true)][string]$Path)

    $image = [System.Drawing.Image]::FromFile($Path)
    try {
        return [ordered]@{
            width = $image.Width
            height = $image.Height
        }
    }
    finally {
        $image.Dispose()
    }
}

$checkedOutCommit = & git -C $repoRoot rev-parse HEAD 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to resolve the checked-out Git commit.'
}
$checkedOutCommit = $checkedOutCommit.Trim().ToLowerInvariant()

if ([string]::IsNullOrWhiteSpace($TestedCommitSha)) {
    $TestedCommitSha = $checkedOutCommit
}
if ([string]::IsNullOrWhiteSpace($SourceHeadSha)) {
    $SourceHeadSha = $TestedCommitSha
}

Assert-CommitSha -Name 'TestedCommitSha' -Value $TestedCommitSha
Assert-CommitSha -Name 'SourceHeadSha' -Value $SourceHeadSha
$TestedCommitSha = $TestedCommitSha.ToLowerInvariant()
$SourceHeadSha = $SourceHeadSha.ToLowerInvariant()

if ($checkedOutCommit -ne $TestedCommitSha) {
    throw "The checked-out commit '$checkedOutCommit' does not match TestedCommitSha '$TestedCommitSha'."
}

if ($SourceHeadSha -ne $TestedCommitSha) {
    & git -C $repoRoot merge-base --is-ancestor $SourceHeadSha $TestedCommitSha
    if ($LASTEXITCODE -ne 0) {
        throw "SourceHeadSha '$SourceHeadSha' is not available as an ancestor of TestedCommitSha '$TestedCommitSha'."
    }
}

$dirtyState = @(& git -C $repoRoot status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to inspect the working tree state.'
}
$isDirty = $dirtyState.Count -gt 0
if ($isDirty -and -not $AllowDirtyWorkingTree) {
    throw 'Canonical Whisper UI evidence requires a clean working tree. Commit the tested slice or pass -AllowDirtyWorkingTree for explicitly non-canonical development evidence.'
}

if ([string]::IsNullOrWhiteSpace($DotnetPath)) {
    $repoDotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $repoDotnet -PathType Leaf) {
        $DotnetPath = $repoDotnet
    }
    else {
        $DotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
    }
}

$resolvedDotnet = (Resolve-Path -LiteralPath $DotnetPath -ErrorAction Stop).Path
$outputRoot = Resolve-RepositoryPath -Path $OutputDirectory
$validationRoot = Resolve-RepositoryPath -Path $ValidationDirectory
$manifestPath = Join-Path $validationRoot 'whisper-ui-evidence.json'
$appPath = Join-Path $repoRoot "src\Soltex.App\bin\$Configuration\net10.0-windows10.0.19041.0\Soltex.dll"
if (-not (Test-Path -LiteralPath $appPath -PathType Leaf)) {
    throw "The built Soltex app was not found at '$appPath'. Build $Configuration before capturing evidence."
}

$mainMatrix = @(
    [ordered]@{ id = 'whisper-dark-standard-100'; theme = 'dark'; profile = 'standard-100'; scale = 100; logicalWidth = 1280; logicalHeight = 820; pixelWidth = 1280; pixelHeight = 820 },
    [ordered]@{ id = 'whisper-light-standard-100'; theme = 'light'; profile = 'standard-100'; scale = 100; logicalWidth = 1280; logicalHeight = 820; pixelWidth = 1280; pixelHeight = 820 },
    [ordered]@{ id = 'whisper-high-contrast-standard-100'; theme = 'high-contrast'; profile = 'standard-100'; scale = 100; logicalWidth = 1280; logicalHeight = 820; pixelWidth = 1280; pixelHeight = 820 },
    [ordered]@{ id = 'whisper-dark-compact-100'; theme = 'dark'; profile = 'compact-100'; scale = 100; logicalWidth = 1100; logicalHeight = 720; pixelWidth = 1100; pixelHeight = 720 },
    [ordered]@{ id = 'whisper-dark-compact-150'; theme = 'dark'; profile = 'compact-150'; scale = 150; logicalWidth = 1100; logicalHeight = 720; pixelWidth = 1650; pixelHeight = 1080 },
    [ordered]@{ id = 'whisper-dark-compact-200'; theme = 'dark'; profile = 'compact-200'; scale = 200; logicalWidth = 1100; logicalHeight = 720; pixelWidth = 2200; pixelHeight = 1440 }
)

$overlayStates = @(
    'listening',
    'command',
    'hands-free-locked',
    'hands-free-warning',
    'hands-free-expired',
    'transcribing',
    'cleaning',
    'inserting',
    'inserted',
    'submitted',
    'copied-fallback',
    'cancelled',
    'error'
)
$overlayMatrix = @(
    $overlayStates | ForEach-Object {
        [ordered]@{ id = "overlay-$($_)-dark-100"; state = $_; theme = 'dark'; scale = 100 }
    }
)
$overlayMatrix += @(
    [ordered]@{ id = 'overlay-listening-light-100'; state = 'listening'; theme = 'light'; scale = 100 },
    [ordered]@{ id = 'overlay-listening-high-contrast-100'; state = 'listening'; theme = 'high-contrast'; scale = 100 },
    [ordered]@{ id = 'overlay-copied-fallback-dark-200'; state = 'copied-fallback'; theme = 'dark'; scale = 200 }
)

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
New-Item -ItemType Directory -Force -Path $validationRoot | Out-Null

$reservedPaths = @($manifestPath)
foreach ($entry in @($mainMatrix) + @($overlayMatrix)) {
    $reservedPaths += (Join-Path $outputRoot ($entry.id + '.png'))
    $reservedPaths += (Join-Path $outputRoot ($entry.id + '.png.error.txt'))
    $reservedPaths += (Join-Path $validationRoot ($entry.id + '.log'))
}
$stalePaths = @($reservedPaths | Where-Object { Test-Path -LiteralPath $_ })
if ($stalePaths.Count -gt 0) {
    throw "Refusing to reuse existing Whisper UI evidence. Choose fresh output directories or remove these task-owned files first:`n$($stalePaths -join "`n")"
}

Add-Type -AssemblyName System.Drawing
$artifacts = @()

foreach ($entry in $mainMatrix) {
    $renderPath = Join-Path $outputRoot ($entry.id + '.png')
    $logPath = Join-Path $validationRoot ($entry.id + '.log')
    $errorPath = $renderPath + '.error.txt'
    Write-Host "Rendering $($entry.id)..."
    $processEvidence = Invoke-OwnedRender -Id $entry.id -LogPath $logPath -Arguments @(
        $appPath,
        '--render-smoke', $renderPath,
        '--panel', 'whisper',
        '--theme', $entry.theme,
        '--profile', $entry.profile
    )

    if (Test-Path -LiteralPath $errorPath -PathType Leaf) {
        throw "Render '$($entry.id)' produced an error sidecar at '$errorPath'."
    }
    if (-not (Test-Path -LiteralPath $renderPath -PathType Leaf)) {
        throw "Render '$($entry.id)' did not create '$renderPath'."
    }

    $file = Get-Item -LiteralPath $renderPath
    $dimensions = Read-PngDimensions -Path $renderPath
    if ($file.Length -le 0 -or $dimensions.width -ne $entry.pixelWidth -or $dimensions.height -ne $entry.pixelHeight) {
        throw "Render '$($entry.id)' was empty or had unexpected dimensions $($dimensions.width)x$($dimensions.height); expected $($entry.pixelWidth)x$($entry.pixelHeight)."
    }

    $artifacts += [ordered]@{
        id = $entry.id
        surface = 'whisper-page'
        state = 'setup'
        theme = $entry.theme
        profile = $entry.profile
        logical_viewport = [ordered]@{ width = $entry.logicalWidth; height = $entry.logicalHeight }
        pixel_size = $dimensions
        scale_percent = $entry.scale
        evidence_class = $(if ($entry.scale -eq 100) { 'native-wpf-controlled' } else { 'synthetic-high-density-render' })
        bytes = $file.Length
        sha256 = (Get-FileHash -LiteralPath $renderPath -Algorithm SHA256).Hash.ToLowerInvariant()
        process = $processEvidence
        file = $entry.id + '.png'
    }
}

foreach ($entry in $overlayMatrix) {
    $renderPath = Join-Path $outputRoot ($entry.id + '.png')
    $logPath = Join-Path $validationRoot ($entry.id + '.log')
    $errorPath = $renderPath + '.error.txt'
    Write-Host "Rendering $($entry.id)..."
    $processEvidence = Invoke-OwnedRender -Id $entry.id -LogPath $logPath -Arguments @(
        $appPath,
        '--whisper-overlay-smoke', $renderPath,
        '--state', $entry.state,
        '--theme', $entry.theme,
        '--density', [string]$entry.scale
    )

    if (Test-Path -LiteralPath $errorPath -PathType Leaf) {
        throw "Render '$($entry.id)' produced an error sidecar at '$errorPath'."
    }
    if (-not (Test-Path -LiteralPath $renderPath -PathType Leaf)) {
        throw "Render '$($entry.id)' did not create '$renderPath'."
    }

    $file = Get-Item -LiteralPath $renderPath
    $dimensions = Read-PngDimensions -Path $renderPath
    $expectedWidth = [int](352 * $entry.scale / 100)
    if ($file.Length -le 0 -or $dimensions.width -ne $expectedWidth -or $dimensions.height -le 0 -or $dimensions.height -gt (600 * $entry.scale / 100)) {
        throw "Overlay '$($entry.id)' was empty or had unexpected dimensions $($dimensions.width)x$($dimensions.height)."
    }

    $artifacts += [ordered]@{
        id = $entry.id
        surface = 'whisper-overlay'
        state = $entry.state
        theme = $entry.theme
        logical_viewport = [ordered]@{
            width = [int]($dimensions.width * 100 / $entry.scale)
            height = [int]($dimensions.height * 100 / $entry.scale)
        }
        pixel_size = $dimensions
        scale_percent = $entry.scale
        evidence_class = $(if ($entry.scale -eq 100) { 'native-wpf-controlled' } else { 'synthetic-high-density-render' })
        bytes = $file.Length
        sha256 = (Get-FileHash -LiteralPath $renderPath -Algorithm SHA256).Hash.ToLowerInvariant()
        process = $processEvidence
        file = $entry.id + '.png'
    }
}

[ordered]@{
    schema_version = 1
    evidence_status = $(if ($isDirty) { 'development-only-dirty-tree' } else { 'canonical-clean-tree' })
    source_head_sha = $SourceHeadSha
    tested_commit_sha = $TestedCommitSha
    captured_at_utc = (Get-Date).ToUniversalTime().ToString('o')
    configuration = $Configuration
    runtime = [ordered]@{
        framework = 'net10.0-windows10.0.19041.0'
        renderer = 'native WPF'
        process_render_mode = 'SoftwareOnly'
        manifest_dpi_awareness = 'PerMonitorV2,PerMonitor'
    }
    claim_boundaries = [ordered]@{
        synthetic_density = 'The 150 and 200 percent artifacts prove deterministic high-density rasterization, not a live Windows per-monitor DPI transition.'
        owner_proof_pending = @(
            'Live 150 and 200 percent Windows per-monitor DPI transitions',
            'Keyboard-only walkthrough',
            'Screen-reader walkthrough',
            'Physical global shortcuts and microphone',
            'Full local model transcription and verified submission'
        )
        content = 'Presenter-controlled labels only; no microphone, transcript, clipboard, target text, or model content is captured.'
    }
    artifacts = $artifacts
} | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 -LiteralPath $manifestPath

Write-Host "Captured and verified $($artifacts.Count) content-free Whisper UI artifacts."
Write-Host "Manifest: $manifestPath"
