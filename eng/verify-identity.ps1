[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$allowlistPath = Join-Path $PSScriptRoot 'identity-allowlist.json'
$policy = Get-Content -LiteralPath $allowlistPath -Raw | ConvertFrom-Json
if ($policy.schemaVersion -ne 1) {
    throw "Unsupported identity allowlist schema: $($policy.schemaVersion)."
}

$tracked = @(
    & git -C $repoRoot ls-files |
        ForEach-Object { $_.Replace('\', '/') }
)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate tracked files for identity verification.'
}

$entries = @($policy.entries)
foreach ($entry in $entries) {
    if ([string]::IsNullOrWhiteSpace([string]$entry.reason)) {
        throw 'Every identity allowlist entry requires a reason.'
    }

    if ($entry.PSObject.Properties.Name -contains 'path') {
        if ($tracked -cnotcontains [string]$entry.path) {
            throw "Identity allowlist path is stale or untracked: $($entry.path)"
        }
    }
    elseif ($entry.PSObject.Properties.Name -contains 'pathPrefix') {
        $prefix = [string]$entry.pathPrefix
        if (-not ($tracked | Where-Object { $_.StartsWith($prefix, [StringComparison]::Ordinal) } | Select-Object -First 1)) {
            throw "Identity allowlist prefix is stale or empty: $prefix"
        }
    }
    else {
        throw 'Every identity allowlist entry requires path or pathPrefix.'
    }
}

$textExtensions = @(
    '.cs', '.csproj', '.json', '.md', '.props', '.ps1', '.sln', '.targets',
    '.txt', '.xaml', '.xml', '.yaml', '.yml'
)
$legacyPattern = [regex]::new(
    'WaveSlate|Wave Slate|waveslate|WAVESLATE_',
    [Text.RegularExpressions.RegexOptions]::CultureInvariant)
$violations = [System.Collections.Generic.List[string]]::new()
$scanned = 0

foreach ($relativePath in $tracked) {
    $extension = [IO.Path]::GetExtension($relativePath)
    if ($textExtensions -cnotcontains $extension) {
        continue
    }

    $scanned++
    $fullPath = Join-Path $repoRoot $relativePath
    $lines = [IO.File]::ReadAllLines($fullPath)
    for ($lineIndex = 0; $lineIndex -lt $lines.Length; $lineIndex++) {
        $line = $lines[$lineIndex]
        $matches = $legacyPattern.Matches($line)
        if ($matches.Count -eq 0) {
            continue
        }

        $allowed = $false
        foreach ($entry in $entries) {
            $pathMatches = $false
            if ($entry.PSObject.Properties.Name -contains 'path') {
                $pathMatches = [string]::Equals(
                    $relativePath,
                    [string]$entry.path,
                    [StringComparison]::Ordinal)
            }
            else {
                $pathMatches = $relativePath.StartsWith(
                    [string]$entry.pathPrefix,
                    [StringComparison]::Ordinal)
            }

            if (-not $pathMatches) {
                continue
            }

            if ($entry.allowAll -eq $true) {
                $allowed = $true
                break
            }

            foreach ($needle in @($entry.lineContains)) {
                if ($line.IndexOf([string]$needle, [StringComparison]::Ordinal) -ge 0) {
                    $allowed = $true
                    break
                }
            }

            if ($allowed) {
                break
            }
        }

        if (-not $allowed) {
            $tokens = ($matches | ForEach-Object { $_.Value } | Sort-Object -Unique) -join ', '
            $violations.Add("${relativePath}:$($lineIndex + 1): unapproved legacy token: $tokens")
        }
    }
}

if ($violations.Count -gt 0) {
    throw "Soltex identity verification failed:`n$($violations -join [Environment]::NewLine)"
}

Write-Host "Identity policy passed: scanned $scanned tracked text files with $($entries.Count) reasoned allowlist entries."
