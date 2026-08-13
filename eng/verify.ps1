[CmdletBinding()]
param(
    [switch]$RunEicar,
    [string]$DotnetPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'verify-identity.ps1')
& (Join-Path $PSScriptRoot 'verify-design-tokens.ps1')

if ([string]::IsNullOrWhiteSpace($DotnetPath)) {
    $repoDotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
    $rootDotnet = if ($env:DOTNET_ROOT) {
        Join-Path $env:DOTNET_ROOT 'dotnet.exe'
    } else {
        $null
    }

    if (Test-Path -LiteralPath $repoDotnet -PathType Leaf) {
        $DotnetPath = $repoDotnet
    } elseif ($rootDotnet -and (Test-Path -LiteralPath $rootDotnet -PathType Leaf)) {
        $DotnetPath = $rootDotnet
    } else {
        $DotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
    }
}
$dotnet = (Resolve-Path -LiteralPath $DotnetPath -ErrorAction Stop).Path

Write-Host "Using dotnet: $dotnet"
& $dotnet --version
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet build (Join-Path $repoRoot 'Soltex.sln') --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($RunEicar) {
    $env:SOLTEX_RUN_EICAR = '1'
}

try {
    $testProjects = @(
        'tests\Soltex.Security.Tests\Soltex.Security.Tests.csproj',
        'tests\Soltex.Security.SupplyChain.Tests\Soltex.Security.SupplyChain.Tests.csproj',
        'tests\Soltex.Security.Hardening.Tests\Soltex.Security.Hardening.Tests.csproj',
        'tests\Soltex.Update.Tests\Soltex.Update.Tests.csproj',
        'tests\Soltex.DeviceFabric.Tests\Soltex.DeviceFabric.Tests.csproj',
        'tests\Soltex.Monitoring.Tests\Soltex.Monitoring.Tests.csproj',
        'tests\Soltex.Audio.Tests\Soltex.Audio.Tests.csproj',
        'tests\Soltex.App.Tests\Soltex.App.Tests.csproj'
    )

    foreach ($project in $testProjects) {
        & $dotnet run --project (Join-Path $repoRoot $project) --configuration Release --no-build
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    exit 0
}
finally {
    Remove-Item Env:SOLTEX_RUN_EICAR -ErrorAction SilentlyContinue
}
