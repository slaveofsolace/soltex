# Publishes the self-contained Soltex desktop build and compiles the per-user
# installer. Produces artifacts/publish/win-x64/Soltex.exe and
# artifacts/installer/Soltex-<version>-win-x64-setup.exe with a SHA-256 record.

[CmdletBinding()]
param(
    [string] $Version = '1.0.0',
    [string] $Runtime = 'win-x64',
    [switch] $SkipInstaller
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "artifacts\publish\$Runtime"
$installerDir = Join-Path $repoRoot 'artifacts\installer'
$appProject = Join-Path $repoRoot 'src\Soltex.App\Soltex.App.csproj'

function Resolve-Tool {
    param([string[]] $Candidates, [string] $Name)

    foreach ($candidate in $Candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    return $null
}

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)][string] $Path)

    $stream = [System.IO.File]::OpenRead($Path)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '')
    }
    finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

# A repo-local SDK, then DOTNET_ROOT, take precedence so the build does not
# depend on whichever runtime happens to be first on PATH. global.json pins the
# feature band, so an older host on PATH fails the restore rather than silently
# producing a different build.
$dotnet = Resolve-Tool -Name 'dotnet' -Candidates @(
    (Join-Path $repoRoot '.dotnet\dotnet.exe'),
    $(if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT 'dotnet.exe' } else { $null })
)
if (-not $dotnet) {
    throw 'Unable to locate a dotnet host. Install the .NET 10 SDK or place one in .dotnet\.'
}

Write-Host "Using dotnet: $dotnet"
& $dotnet --version | Write-Host

& (Join-Path $PSScriptRoot 'make-icon.ps1') | Write-Host

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

& $dotnet publish $appProject `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    --output $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE."
}

$publishedExe = Join-Path $publishDir 'Soltex.exe'
if (-not (Test-Path -LiteralPath $publishedExe)) {
    throw "Expected published executable was not produced: $publishedExe"
}

$publishedHash = Get-Sha256Hex -Path $publishedExe
Write-Host "Published: $publishedExe"
Write-Host "  bytes : $((Get-Item -LiteralPath $publishedExe).Length)"
Write-Host "  sha256: $publishedHash"

if ($SkipInstaller) {
    return
}

$iscc = Resolve-Tool -Name 'ISCC' -Candidates @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
if (-not $iscc) {
    throw 'Unable to locate ISCC.exe. Install Inno Setup 6 or pass -SkipInstaller.'
}

Write-Host "Using Inno Setup: $iscc"

& $iscc `
    "/DSourceExe=$publishedExe" `
    "/DAppVersion=$Version" `
    (Join-Path $PSScriptRoot 'soltex.iss')
if ($LASTEXITCODE -ne 0) {
    throw "Installer compilation failed with exit code $LASTEXITCODE."
}

$setup = Join-Path $installerDir "Soltex-$Version-$Runtime-setup.exe"
if (-not (Test-Path -LiteralPath $setup)) {
    throw "Expected installer was not produced: $setup"
}

$setupItem = Get-Item -LiteralPath $setup
$setupHash = Get-Sha256Hex -Path $setup

$checksumPath = "$setup.sha256"
"$setupHash *$($setupItem.Name)" | Set-Content -LiteralPath $checksumPath -Encoding ascii

Write-Host ''
Write-Host "Installer: $setup"
Write-Host "  bytes : $($setupItem.Length)"
Write-Host "  sha256: $setupHash"
Write-Host "  digest: $checksumPath"
Write-Host ''
Write-Host 'This installer is not code-signed. SmartScreen will warn on first run.'
