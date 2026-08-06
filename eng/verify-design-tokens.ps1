# Fails if a raw colour literal appears in any XAML file other than the design
# token dictionary. This keeps the visual language in one place: new views and
# controls must resolve colour through a token or semantic brush from
# src/Soltex.App/Themes/Tokens.xaml. See docs/DESIGN_SYSTEM.md.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$tokenFile = 'src/Soltex.App/Themes/Tokens.xaml'

$tracked = @(
    & git -C $repoRoot ls-files 'src/Soltex.App/*.xaml' |
        ForEach-Object { $_.Replace('\', '/') }
)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate tracked XAML for design-token verification.'
}

# WPF colour literals: #RGB, #ARGB, #RRGGBB, #AARRGGBB.
$hexPattern = [regex]::new(
    '#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{4}|[0-9A-Fa-f]{3})\b',
    [Text.RegularExpressions.RegexOptions]::CultureInvariant)

$violations = [System.Collections.Generic.List[string]]::new()
$scanned = 0

foreach ($relativePath in $tracked) {
    if ($relativePath -eq $tokenFile) {
        continue
    }

    $scanned++
    $fullPath = Join-Path $repoRoot $relativePath
    $lines = [IO.File]::ReadAllLines($fullPath)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        foreach ($match in $hexPattern.Matches($lines[$i])) {
            $violations.Add("${relativePath}:$($i + 1): raw colour '$($match.Value)' — use a token from $tokenFile")
        }
    }
}

if ($violations.Count -gt 0) {
    throw "Design-token verification failed:`n$($violations -join [Environment]::NewLine)"
}

Write-Host "Design-token policy passed: scanned $scanned XAML file(s); colour lives only in $tokenFile."
