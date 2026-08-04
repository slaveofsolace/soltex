[CmdletBinding()]
param(
    [switch]$RunEicar
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'verify-identity.ps1')

$dotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}

& $dotnet build (Join-Path $repoRoot 'Soltex.sln') --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($RunEicar) {
    $env:SOLTEX_RUN_EICAR = '1'
}

try {
    & $dotnet run --project (Join-Path $repoRoot 'tests\Soltex.Security.Tests\Soltex.Security.Tests.csproj') --configuration Release --no-build
    exit $LASTEXITCODE
}
finally {
    Remove-Item Env:SOLTEX_RUN_EICAR -ErrorAction SilentlyContinue
}
