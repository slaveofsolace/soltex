[CmdletBinding()]
param(
    [string]$OutputDirectory = 'artifacts\visual',
    [string]$ValidationDirectory = 'artifacts\validation',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$SourceHeadSha,
    [string]$TestedCommitSha,
    [string]$DotnetPath
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

$checkedOutCommit = & git -C $repoRoot rev-parse HEAD 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to resolve the checked-out Git commit.'
}
$checkedOutCommit = $checkedOutCommit.Trim()

if ([string]::IsNullOrWhiteSpace($TestedCommitSha)) {
    $TestedCommitSha = $checkedOutCommit
}
if ([string]::IsNullOrWhiteSpace($SourceHeadSha)) {
    $SourceHeadSha = $TestedCommitSha
}

Assert-CommitSha -Name 'TestedCommitSha' -Value $TestedCommitSha
Assert-CommitSha -Name 'SourceHeadSha' -Value $SourceHeadSha

$checkedOutCommit = $checkedOutCommit.ToLowerInvariant()
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
$manifestPath = Join-Path $validationRoot 'render-matrix.json'
$projectPath = Join-Path $repoRoot 'src\Soltex.App\Soltex.App.csproj'

$matrix = @(
    [ordered]@{ id = 'home-default'; panel = 'home'; state = 'default'; file = 'home-current-source.png' },
    [ordered]@{ id = 'command-palette'; panel = 'command-palette'; state = 'expanded'; file = 'command-palette-current-source.png' },
    [ordered]@{ id = 'monitoring-default'; panel = 'monitoring'; state = 'default'; file = 'monitoring-current-source.png' },
    [ordered]@{ id = 'monitoring-details'; panel = 'monitoring-details'; state = 'expanded'; file = 'monitoring-details-current-source.png' },
    [ordered]@{ id = 'monitoring-benchmark'; panel = 'monitoring-benchmark'; state = 'idle'; file = 'monitoring-benchmark-current-source.png' },
    [ordered]@{ id = 'applications-default'; panel = 'applications'; state = 'default'; file = 'applications-current-source.png' },
    [ordered]@{ id = 'applications-services'; panel = 'applications-services'; state = 'expanded'; file = 'applications-services-current-source.png' },
    [ordered]@{ id = 'activity-default'; panel = 'activity'; state = 'default'; file = 'activity-current-source.png' },
    [ordered]@{ id = 'settings-default'; panel = 'settings'; state = 'default'; file = 'settings-current-source.png' },
    [ordered]@{ id = 'devices-default'; panel = 'devices'; state = 'default'; file = 'devices-current-source.png' },
    [ordered]@{ id = 'mixer-default'; panel = 'mixer'; state = 'default'; file = 'mixer-current-source.png' },
    [ordered]@{ id = 'mixer-devices'; panel = 'mixer-devices'; state = 'expanded'; file = 'mixer-devices-current-source.png' },
    [ordered]@{ id = 'mixer-more'; panel = 'mixer-more'; state = 'expanded'; file = 'mixer-more-current-source.png' },
    [ordered]@{ id = 'clips-default'; panel = 'clips'; state = 'default'; file = 'clips-current-source.png' },
    [ordered]@{ id = 'security-default'; panel = 'security'; state = 'default'; file = 'security-current-source.png' },
    [ordered]@{ id = 'security-activity'; panel = 'security-activity'; state = 'expanded'; file = 'security-activity-current-source.png' },
    [ordered]@{ id = 'remote-default'; panel = 'remote'; state = 'default'; file = 'remote-assist-current-source.png' },
    [ordered]@{ id = 'whisper-default'; panel = 'whisper'; state = 'capture-setup'; file = 'whisper-current-source.png' },
    [ordered]@{ id = 'whisper-personalize'; panel = 'whisper-personalize'; state = 'personalization'; file = 'whisper-personalize-current-source.png' },
    [ordered]@{ id = 'whisper-library'; panel = 'whisper-library'; state = 'snippets'; file = 'whisper-library-current-source.png' },
    [ordered]@{ id = 'whisper-library-styles'; panel = 'whisper-library-styles'; state = 'styles'; file = 'whisper-library-styles-current-source.png' },
    [ordered]@{ id = 'whisper-library-apps'; panel = 'whisper-library-apps'; state = 'application-rules'; file = 'whisper-library-apps-current-source.png' },
    [ordered]@{ id = 'whisper-scratchpad'; panel = 'whisper-scratchpad'; state = 'session-memory'; file = 'whisper-scratchpad-current-source.png' },
    [ordered]@{ id = 'whisper-history'; panel = 'whisper-history'; state = 'empty-session'; file = 'whisper-history-current-source.png' },
    [ordered]@{ id = 'whisper-privacy'; panel = 'whisper-privacy'; state = 'safe-defaults'; file = 'whisper-privacy-current-source.png' },
    [ordered]@{ id = 'whisper-privacy-encrypted'; panel = 'whisper-privacy-encrypted'; state = 'encrypted-retention'; file = 'whisper-privacy-encrypted-current-source.png' },
    [ordered]@{ id = 'whisper-privacy-warning'; panel = 'whisper-privacy-warning'; state = 'auto-send-consent'; file = 'whisper-privacy-warning-current-source.png' },
    [ordered]@{ id = 'updates-default'; panel = 'update'; state = 'default'; file = 'updates-current-source.png' }
)

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
New-Item -ItemType Directory -Force -Path $validationRoot | Out-Null

$reservedPaths = @($manifestPath)
foreach ($entry in $matrix) {
    $renderPath = Join-Path $outputRoot $entry.file
    $reservedPaths += $renderPath
    $reservedPaths += ($renderPath + '.error.txt')
    $reservedPaths += (Join-Path $validationRoot ("render-{0}.log" -f $entry.id))
}

$stalePaths = @($reservedPaths | Where-Object { Test-Path -LiteralPath $_ })
if ($stalePaths.Count -gt 0) {
    throw "Refusing to reuse existing render evidence. Choose fresh output directories or remove these task-owned files first:`n$($stalePaths -join "`n")"
}

$artifacts = @()
Add-Type -AssemblyName System.Drawing

foreach ($entry in $matrix) {
    $renderPath = Join-Path $outputRoot $entry.file
    $errorPath = $renderPath + '.error.txt'
    $logPath = Join-Path $validationRoot ("render-{0}.log" -f $entry.id)

    Write-Host "Rendering $($entry.id) from panel '$($entry.panel)'..."
    & $resolvedDotnet run --project $projectPath --configuration $Configuration --no-build -- --render-smoke $renderPath --panel $entry.panel 2>&1 |
        Tee-Object -FilePath $logPath
    $renderExitCode = $LASTEXITCODE

    if ($renderExitCode -ne 0) {
        throw "Render '$($entry.id)' failed with exit code $renderExitCode. See '$logPath'."
    }
    if (Test-Path -LiteralPath $errorPath -PathType Leaf) {
        Write-Host "Render error file: $errorPath"
        Get-Content -LiteralPath $errorPath
        throw "Render '$($entry.id)' produced an error sidecar."
    }
    if (-not (Test-Path -LiteralPath $renderPath -PathType Leaf)) {
        throw "Render '$($entry.id)' did not create '$renderPath'."
    }

    $file = Get-Item -LiteralPath $renderPath
    if ($file.Length -le 0) {
        throw "Render '$($entry.id)' produced an empty PNG."
    }

    $image = [System.Drawing.Image]::FromFile($renderPath)
    try {
        if ($image.Width -ne 1280 -or $image.Height -ne 820) {
            throw "Render '$($entry.id)' is $($image.Width)x$($image.Height); expected 1280x820."
        }
    }
    finally {
        $image.Dispose()
    }

    $hash = Get-FileHash -LiteralPath $renderPath -Algorithm SHA256
    $artifacts += [ordered]@{
        id = $entry.id
        panel = $entry.panel
        state = $entry.state
        file = $entry.file
        bytes = $file.Length
        sha256 = $hash.Hash.ToLowerInvariant()
    }
}

[ordered]@{
    schema_version = 1
    source_head_sha = $SourceHeadSha
    tested_commit_sha = $TestedCommitSha
    captured_at_utc = (Get-Date).ToUniversalTime().ToString('o')
    configuration = $Configuration
    runtime = [ordered]@{
        framework = 'net10.0-windows10.0.19041.0'
        renderer = 'native WPF'
        process_render_mode = 'SoftwareOnly'
    }
    viewport = [ordered]@{
        width = 1280
        height = 820
    }
    artifacts = $artifacts
} | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 -LiteralPath $manifestPath

Write-Host "Captured and verified $($artifacts.Count) native UI states."
Write-Host "Manifest: $manifestPath"
