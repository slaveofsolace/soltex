[CmdletBinding()]
param(
    [string]$OutputDirectory = 'artifacts\validation',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [Parameter(Mandatory = $true)]
    [string]$SourceHeadSha,
    [Parameter(Mandatory = $true)]
    [string]$TestedCommitSha,
    [string]$DotnetPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-CommitSha([string]$Name, [string]$Value) {
    if ($Value -notmatch '^[0-9a-fA-F]{40}$') {
        throw "$Name must be a full 40-character hexadecimal commit SHA."
    }
}

Assert-CommitSha 'SourceHeadSha' $SourceHeadSha
Assert-CommitSha 'TestedCommitSha' $TestedCommitSha

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$outputRoot = [IO.Path]::GetFullPath(
    $(if ([IO.Path]::IsPathRooted($OutputDirectory)) {
        $OutputDirectory
    } else {
        Join-Path $repoRoot $OutputDirectory
    }))
$reportPath = Join-Path $outputRoot 'runtime-cost.json'
$errorPath = $reportPath + '.error.txt'
$summaryPath = Join-Path $outputRoot 'runtime-cost-summary.log'
$progressPath = $reportPath + '.progress.log'
$applicationPath = Join-Path $repoRoot "src\Soltex.App\bin\$Configuration\net10.0-windows10.0.19041.0\Soltex.dll"

if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
    throw "The runtime probe requires an existing $Configuration Soltex.dll build."
}
if ([string]::IsNullOrWhiteSpace($DotnetPath)) {
    $DotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
}
$DotnetPath = [IO.Path]::GetFullPath($DotnetPath)
if (-not (Test-Path -LiteralPath $DotnetPath -PathType Leaf)) {
    throw "The requested dotnet host does not exist: $DotnetPath"
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
foreach ($reservedPath in @($reportPath, $errorPath, $summaryPath, $progressPath)) {
    if (Test-Path -LiteralPath $reservedPath) {
        throw "Refusing to reuse runtime evidence: $reservedPath"
    }
}

$previousReportPath = $env:SOLTEX_RUNTIME_REPORT_PATH
$previousSourceHead = $env:SOLTEX_SOURCE_HEAD_SHA
$previousTestedCommit = $env:SOLTEX_TESTED_COMMIT_SHA
$process = $null
$processId = 0
try {
    $env:SOLTEX_RUNTIME_REPORT_PATH = $reportPath
    $env:SOLTEX_SOURCE_HEAD_SHA = $SourceHeadSha.ToLowerInvariant()
    $env:SOLTEX_TESTED_COMMIT_SHA = $TestedCommitSha.ToLowerInvariant()
    $runtimeArguments = "exec `"$applicationPath`" --runtime-probe"
    $process = Start-Process `
        -FilePath $DotnetPath `
        -ArgumentList $runtimeArguments `
        -WorkingDirectory $repoRoot `
        -WindowStyle Hidden `
        -PassThru
    $processHandle = $process.Handle
    $processId = $process.Id
    Write-Host "Runtime probe PID: $($process.Id)"
    if (-not $process.WaitForExit(45000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw "The task-owned runtime probe exceeded its 45-second bound."
    }

    $process.Refresh()
    if ($process.ExitCode -ne 0) {
        $detail = if (Test-Path -LiteralPath $errorPath) {
            Get-Content -LiteralPath $errorPath -Raw
        } else {
            'No error sidecar was produced.'
        }
        throw "The runtime probe exited with code $($process.ExitCode). $detail"
    }
}
finally {
    $env:SOLTEX_RUNTIME_REPORT_PATH = $previousReportPath
    $env:SOLTEX_SOURCE_HEAD_SHA = $previousSourceHead
    $env:SOLTEX_TESTED_COMMIT_SHA = $previousTestedCommit
    if ($null -ne $process) {
        $process.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
    throw 'The runtime probe exited without its JSON report.'
}

$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if ($report.schemaVersion -ne 2) {
    throw "Unexpected runtime report schema: $($report.schemaVersion)"
}
if ($report.sourceHeadSha -ne $SourceHeadSha.ToLowerInvariant() -or
    $report.testedCommitSha -ne $TestedCommitSha.ToLowerInvariant()) {
    throw 'Runtime report commit identity does not match the requested source/tested commits.'
}
if ($report.startupMilliseconds -lt 0 -or $report.startupMilliseconds -gt 20000) {
    throw 'Runtime startup completion was outside its 20-second bound.'
}

$samples = @($report.samples)
$expectedStates = @(
    'visible-idle',
    'minimized-transition',
    'minimized-steady',
    'hidden-notification-area')
if ($samples.Count -ne $expectedStates.Count) {
    throw "Expected $($expectedStates.Count) runtime samples; found $($samples.Count)."
}
foreach ($expectedState in $expectedStates) {
    $sample = @($samples | Where-Object { $_.state -eq $expectedState })
    if ($sample.Count -ne 1) {
        throw "Runtime report does not contain exactly one '$expectedState' sample."
    }
    if ($sample[0].wallMilliseconds -lt 1800 -or $sample[0].wallMilliseconds -gt 5000) {
        throw "Runtime sample '$expectedState' is outside its bounded measurement window."
    }
    if ($sample[0].normalizedCpuPercent -lt 0 -or $sample[0].normalizedCpuPercent -gt 100) {
        throw "Runtime sample '$expectedState' contains an invalid normalized CPU value."
    }
    if ($sample[0].workingSetBytes -le 0 -or $sample[0].privateMemoryBytes -le 0) {
        throw "Runtime sample '$expectedState' did not record process memory."
    }
    if ($sample[0].uiThreadId -le 0 -or
        $sample[0].uiThreadCpuMilliseconds -lt 0 -or
        $sample[0].topCpuThreadId -le 0 -or
        $sample[0].topCpuThreadCpuMilliseconds -lt 0 -or
        [string]::IsNullOrWhiteSpace([string]$sample[0].topCpuThreadRole)) {
        throw "Runtime sample '$expectedState' did not record valid thread attribution."
    }
}
$visibleSample = $samples | Where-Object { $_.state -eq 'visible-idle' }
if (-not [bool]$visibleSample.performanceSamplingActive) {
    throw 'Visible-idle evidence did not confirm an active Performance sampler.'
}
foreach ($suspendedState in @(
        'minimized-transition',
        'minimized-steady',
        'hidden-notification-area')) {
    $suspendedSample = $samples | Where-Object { $_.state -eq $suspendedState }
    if ([bool]$suspendedSample.performanceSamplingActive) {
        throw "Runtime sample '$suspendedState' retained the Performance sampler."
    }
}
if ($report.navigation.expectedTransitionCount -le 0 -or
    $report.navigation.transitionCount -ne $report.navigation.expectedTransitionCount -or
    $report.navigation.totalMilliseconds -lt 0 -or
    $report.navigation.maximumMilliseconds -lt 0) {
    throw 'Runtime navigation evidence is incomplete or invalid.'
}

$summary = @(
    "source_head=$($report.sourceHeadSha)",
    "tested_commit=$($report.testedCommitSha)",
    "probe_pid=$processId",
    "startup_ms=$([double]$report.startupMilliseconds)",
    "navigation_transitions=$($report.navigation.transitionCount)",
    "navigation_mean_ms=$([double]$report.navigation.meanMilliseconds)",
    "navigation_max_ms=$([double]$report.navigation.maximumMilliseconds)"
)
foreach ($sample in $samples) {
    $summary += "$($sample.state)_cpu_percent=$([double]$sample.normalizedCpuPercent)"
    $summary += "$($sample.state)_working_set_bytes=$([long]$sample.workingSetBytes)"
    $summary += "$($sample.state)_private_bytes=$([long]$sample.privateMemoryBytes)"
    $summary += "$($sample.state)_performance_sampling=$([bool]$sample.performanceSamplingActive)"
    $summary += "$($sample.state)_ui_thread_cpu_ms=$([double]$sample.uiThreadCpuMilliseconds)"
    $summary += "$($sample.state)_top_thread_id=$([int]$sample.topCpuThreadId)"
    $summary += "$($sample.state)_top_thread_role=$([string]$sample.topCpuThreadRole)"
    $summary += "$($sample.state)_top_thread_cpu_ms=$([double]$sample.topCpuThreadCpuMilliseconds)"
}
$summary | Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Host "Runtime lifecycle evidence passed."
Write-Host "Report: $reportPath"
Write-Host "Summary: $summaryPath"
