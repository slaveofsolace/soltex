[CmdletBinding()]
param(
    [switch]$RunEicar
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}

& $dotnet build (Join-Path $repoRoot 'WaveSlate.sln') --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($RunEicar) {
    $env:WAVESLATE_RUN_EICAR = '1'
}

try {
    & $dotnet run --project (Join-Path $repoRoot 'tests\WaveSlate.Security.Tests\WaveSlate.Security.Tests.csproj') --configuration Release --no-build
    exit $LASTEXITCODE
}
finally {
    Remove-Item Env:WAVESLATE_RUN_EICAR -ErrorAction SilentlyContinue
}
