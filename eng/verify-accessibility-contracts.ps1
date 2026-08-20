[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$appRoot = Join-Path $repoRoot 'src\Soltex.App'
$xamlFiles = @(Get-ChildItem -LiteralPath $appRoot -Recurse -Filter '*.xaml' -File)
if ($xamlFiles.Count -eq 0) {
    throw 'No Soltex application XAML files were found.'
}

$interactiveKinds = @(
    'Button',
    'CheckBox',
    'ComboBox',
    'DataGrid',
    'ListBox',
    'PasswordBox',
    'RadioButton',
    'Slider',
    'TextBox',
    'ToggleButton'
)
$failures = [System.Collections.Generic.List[string]]::new()
$interactiveCount = 0

function Get-AttributeValue {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlElement]$Element,
        [Parameter(Mandatory = $true)]
        [string]$LocalName
    )

    $attribute = @($Element.Attributes | Where-Object { $_.LocalName -eq $LocalName } |
        Select-Object -First 1)
    if ($attribute.Count -eq 0) {
        return ''
    }

    return [string]$attribute[0].Value
}

foreach ($file in $xamlFiles) {
    try {
        [xml]$document = Get-Content -LiteralPath $file.FullName -Raw
    }
    catch {
        $failures.Add("$($file.FullName): XAML could not be parsed: $($_.Exception.Message)")
        continue
    }

    foreach ($element in @($document.SelectNodes('//*'))) {
        if ($element.LocalName -notin $interactiveKinds) {
            continue
        }

        $interactiveCount++
        $accessibleName = Get-AttributeValue -Element $element -LocalName 'AutomationProperties.Name'
        $staticContent = Get-AttributeValue -Element $element -LocalName 'Content'
        $hasStaticButtonContent =
            $element.LocalName -eq 'Button' -and
            -not [string]::IsNullOrWhiteSpace($staticContent) -and
            -not $staticContent.StartsWith('{', [StringComparison]::Ordinal)
        if (-not [string]::IsNullOrWhiteSpace($accessibleName) -or $hasStaticButtonContent) {
            continue
        }

        $controlName = Get-AttributeValue -Element $element -LocalName 'Name'
        if ([string]::IsNullOrWhiteSpace($controlName)) {
            $controlName = '(unnamed)'
        }
        $relativePath = [IO.Path]::GetRelativePath($repoRoot, $file.FullName)
        $failures.Add(
            "${relativePath}: $($element.LocalName) '$controlName' requires an explicit AutomationProperties.Name.")
    }
}

[xml]$mainWindow = Get-Content -LiteralPath (Join-Path $appRoot 'MainWindow.xaml') -Raw
$palette = $mainWindow.SelectSingleNode(
    '//*[local-name()="Grid" and @*[local-name()="Name" and .="CommandPaletteOverlay"]]')
if ($null -eq $palette) {
    $failures.Add('MainWindow.xaml: the command-palette focus scope is missing.')
}
else {
    foreach ($property in @(
            'KeyboardNavigation.TabNavigation',
            'KeyboardNavigation.ControlTabNavigation')) {
        if ((Get-AttributeValue -Element $palette -LocalName $property) -ne 'Cycle') {
            $failures.Add("MainWindow.xaml: CommandPaletteOverlay must set $property to Cycle.")
        }
    }
}

$emptyState = $mainWindow.SelectSingleNode(
    '//*[local-name()="TextBlock" and @*[local-name()="Name" and .="CommandEmptyText"]]')
if ($null -eq $emptyState -or
    (Get-AttributeValue -Element $emptyState -LocalName 'AutomationProperties.LiveSetting') -ne 'Polite') {
    $failures.Add('MainWindow.xaml: command-palette empty results must be announced politely.')
}

$windowCode = Get-Content -LiteralPath (Join-Path $appRoot 'MainWindow.xaml.cs') -Raw
if ($windowCode.IndexOf('SystemParameters.ClientAreaAnimation', [StringComparison]::Ordinal) -lt 0) {
    $failures.Add('MainWindow.xaml.cs: workspace motion must honor the Windows client-area animation preference.')
}

[xml]$manifest = Get-Content -LiteralPath (Join-Path $appRoot 'app.manifest') -Raw
$dpiAwareness = $manifest.SelectSingleNode('//*[local-name()="dpiAwareness"]')
$legacyDpiAware = $manifest.SelectSingleNode('//*[local-name()="dpiAware"]')
if ($null -eq $dpiAwareness -or
    $dpiAwareness.InnerText -notmatch '(^|,)PerMonitorV2(,|$)') {
    $failures.Add('app.manifest: the shipped app must declare PerMonitorV2 DPI awareness.')
}
if ($null -eq $legacyDpiAware -or
    $legacyDpiAware.InnerText -ne 'true/pm') {
    $failures.Add('app.manifest: the compatibility DPI declaration must remain per-monitor aware.')
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    throw "Accessibility contract failed with $($failures.Count) issue(s)."
}

Write-Host "Accessibility contract passed: $interactiveCount interactive controls across $($xamlFiles.Count) XAML file(s); command focus cycles, empty results announce politely, reduced motion follows Windows, and the app declares PerMonitorV2 DPI awareness."
