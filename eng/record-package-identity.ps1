[CmdletBinding()]
param(
    [string]$ExecutablePath = 'artifacts\publish\win-x64\Soltex.exe',
    [string]$RenderPath = 'artifacts\validation\package-home.png',
    [string]$OutputPath = 'artifacts\validation\package-smoke.json',
    [string]$Runtime = 'win-x64',
    [string]$SourceHeadSha,
    [string]$TestedCommitSha,
    [string]$EventName,
    [string]$HeadRef
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

$resolvedExecutable = Resolve-RepositoryPath -Path $ExecutablePath
$resolvedRender = Resolve-RepositoryPath -Path $RenderPath
$resolvedOutput = Resolve-RepositoryPath -Path $OutputPath
if (-not (Test-Path -LiteralPath $resolvedExecutable -PathType Leaf)) {
    throw "Published executable was not found: '$resolvedExecutable'."
}
if (-not (Test-Path -LiteralPath $resolvedRender -PathType Leaf)) {
    throw "Published package render was not found: '$resolvedRender'."
}
if (Test-Path -LiteralPath $resolvedOutput) {
    throw "Refusing to overwrite existing package identity evidence: '$resolvedOutput'."
}

$outputDirectory = Split-Path -Parent $resolvedOutput
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$item = Get-Item -LiteralPath $resolvedExecutable
$hash = Get-FileHash -LiteralPath $resolvedExecutable -Algorithm SHA256
$renderItem = Get-Item -LiteralPath $resolvedRender
$renderHash = Get-FileHash -LiteralPath $resolvedRender -Algorithm SHA256
Add-Type -AssemblyName System.Drawing
$renderImage = [System.Drawing.Image]::FromFile($resolvedRender)
try {
    if ($renderImage.Width -ne 1280 -or $renderImage.Height -ne 820) {
        throw "Published package render is $($renderImage.Width)x$($renderImage.Height); expected 1280x820."
    }
}
finally {
    $renderImage.Dispose()
}
$signature = Get-AuthenticodeSignature -FilePath $resolvedExecutable
$signatureStatus = $signature.Status.ToString()
if ($signatureStatus -notin @('Valid', 'NotSigned')) {
    throw "Published package has an unacceptable Authenticode status: $signatureStatus."
}

[ordered]@{
    schema_version = 2
    commit = $TestedCommitSha
    source_head_sha = $SourceHeadSha
    tested_commit_sha = $TestedCommitSha
    event_name = $EventName
    head_ref = $HeadRef
    runtime = $Runtime
    file = $item.Name
    length = $item.Length
    sha256 = $hash.Hash.ToLowerInvariant()
    authenticode_status = $signatureStatus
    signed = ($signature.Status -eq 'Valid')
    render = [ordered]@{
        panel = 'home'
        file = $renderItem.Name
        length = $renderItem.Length
        sha256 = $renderHash.Hash.ToLowerInvariant()
        width = 1280
        height = 820
    }
    recorded_at_utc = (Get-Date).ToUniversalTime().ToString('o')
} | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 -LiteralPath $resolvedOutput

Write-Host "Recorded package identity: $resolvedOutput"
