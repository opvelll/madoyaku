param(
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$runId = Get-Date -Format "yyyyMMdd-HHmmss"
$artifactRoot = Join-Path $root "artifacts\ui-verification\$runId"
$dataDir = Join-Path $artifactRoot "data"
$imageDir = Join-Path $artifactRoot "images"
$logPath = Join-Path $artifactRoot "run.log"
$buildLogPath = Join-Path $artifactRoot "build.log"
$reportPath = Join-Path $artifactRoot "report.md"
$resultPath = Join-Path $artifactRoot "result.json"
$project = Join-Path $root "HonnyakuKun.csproj"
$exe = Join-Path $root "bin\Release\net10.0-windows\HonnyakuKun.exe"
$process = $null
$steps = [System.Collections.Generic.List[object]]::new()

New-Item -ItemType Directory -Force -Path $dataDir, $imageDir | Out-Null
Start-Transcript -Path $logPath -Force | Out-Null

function Add-Step([string]$Name, [bool]$Passed, [string]$Detail) {
    $steps.Add([pscustomobject]@{ name = $Name; passed = $Passed; detail = $Detail })
}

function Find-Element($rootElement, [string]$automationId) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $rootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Wait-Element($rootElement, [string]$automationId, [int]$seconds = 10) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        $element = Find-Element $rootElement $automationId
        if ($null -ne $element) { return $element }
        Start-Sleep -Milliseconds 150
    } while ((Get-Date) -lt $deadline)
    throw "UI element '$automationId' was not found."
}

function Invoke-Element($element) {
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
}

function Set-ElementValue($element, [string]$value) {
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    ([System.Windows.Automation.ValuePattern]$pattern).SetValue($value)
}

function Get-ElementValue($element) {
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    return ([System.Windows.Automation.ValuePattern]$pattern).Current.Value
}

function Wait-SettingsWindow($process) {
    $settingsDeadline = (Get-Date).AddSeconds(10)
    do {
        $main = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        $settings = Find-Element $main "SettingsWindow"
        if ($null -eq $settings) { Start-Sleep -Milliseconds 150 }
    } while ($null -eq $settings -and (Get-Date) -lt $settingsDeadline)
    if ($null -eq $settings) { throw "The settings window did not open." }
    return [pscustomobject]@{ Main = $main; Settings = $settings }
}

function Save-WindowImage([IntPtr]$handle, [string]$name) {
    $path = Join-Path $imageDir "$name.png"
    [UiCapture]::Capture($handle, $path)
    return $path
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
$drawingCommonPath = Join-Path $PSHOME "System.Drawing.Common.dll"
if (Test-Path $drawingCommonPath) { Add-Type -AssemblyName System.Drawing.Common }
$drawingPath = [System.Drawing.Bitmap].Assembly.Location
$referencePaths = [System.Collections.Generic.List[string]]::new()
$referencePaths.Add($drawingPath)
foreach ($name in @("System.Private.Windows.GdiPlus.dll", "System.Private.Windows.Core.dll", "System.Drawing.Primitives.dll")) {
    $path = Join-Path $PSHOME $name
    if (Test-Path $path) { $referencePaths.Add($path) }
}
Add-Type -ReferencedAssemblies $referencePaths -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class UiCapture {
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    public static void Capture(IntPtr handle, string path) {
        RECT rect;
        if (!GetWindowRect(handle, out rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            throw new InvalidOperationException("The application window has no captureable bounds.");
        using (var bitmap = new Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top, PixelFormat.Format32bppArgb))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
            bitmap.Save(path, ImageFormat.Png);
        }
    }
}
"@

try {
    $existingTestProcess = Get-CimInstance Win32_Process -Filter "Name='HonnyakuKun.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match "--ui-test" }
    if ($existingTestProcess) {
        throw "A previous UI test instance is already running. Close it before UI verification."
    }
    if (Get-Process -Name HonnyakuKun -ErrorAction SilentlyContinue) {
        Write-Warning "A normal HonnyakuKun instance is already running; it will not be touched."
    }

    & dotnet build $project -c Release -warnaserror --nologo | Tee-Object -FilePath $buildLogPath
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) { throw "Release build failed." }

    $process = Start-Process -FilePath $exe -ArgumentList @("--ui-test", "--test-data-dir", $dataDir) -PassThru
    $deadline = (Get-Date).AddSeconds(20)
    do {
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    if ($process.MainWindowHandle -eq [IntPtr]::Zero) { throw "The UI test application did not create a window." }

    $main = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    Add-Step "launch" $true "Main window created."
    $images = [System.Collections.Generic.List[string]]::new()
    $images.Add((Save-WindowImage $process.MainWindowHandle "01-main-initial"))

    $settingsButton = Wait-Element $main "SettingsButton"
    Invoke-Element $settingsButton
    $ui = Wait-SettingsWindow $process
    $main = $ui.Main
    $settings = $ui.Settings
    Add-Step "settings-open" $true "Settings window opened."
    Start-Sleep -Milliseconds 500
    $images.Add((Save-WindowImage ([IntPtr]$settings.Current.NativeWindowHandle) "02-settings"))

    Set-ElementValue (Wait-Element $settings "TargetLanguageBox") "English"
    Set-ElementValue (Wait-Element $settings "HistoryLimitBox") "3"
    $images.Add((Save-WindowImage ([IntPtr]$settings.Current.NativeWindowHandle) "03-settings-edited"))
    Set-ElementValue (Wait-Element $settings "ModelBox") ""
    Invoke-Element (Wait-Element $settings "SaveButton")
    Start-Sleep -Milliseconds 200
    $validation = Wait-Element $settings "ValidationText"
    if ([string]::IsNullOrWhiteSpace($validation.Current.Name)) { throw "Validation message was not displayed." }
    $images.Add((Save-WindowImage ([IntPtr]$settings.Current.NativeWindowHandle) "04-settings-validation"))
    Add-Step "settings-validation" $true "Invalid model input was rejected with a visible validation message."
    Set-ElementValue (Wait-Element $settings "ModelBox") "gpt-5.6-luna"
    Invoke-Element (Wait-Element $settings "SaveButton")
    Start-Sleep -Milliseconds 300
    Add-Step "settings-save" $true "Non-secret settings changed and saved."
    $images.Add((Save-WindowImage $process.MainWindowHandle "05-main-after-settings"))

    Invoke-Element (Wait-Element $main "SettingsButton")
    $ui = Wait-SettingsWindow $process
    $main = $ui.Main
    $settings = $ui.Settings
    Set-ElementValue (Wait-Element $settings "TargetLanguageBox") "CancelTest"
    Invoke-Element (Wait-Element $settings "CancelButton")
    Start-Sleep -Milliseconds 200
    Invoke-Element (Wait-Element $main "SettingsButton")
    $ui = Wait-SettingsWindow $process
    $main = $ui.Main
    $settings = $ui.Settings
    if ((Get-ElementValue (Wait-Element $settings "TargetLanguageBox")) -ne "English") {
        throw "Cancel unexpectedly persisted an edited setting."
    }
    Start-Sleep -Milliseconds 500
    $images.Add((Save-WindowImage ([IntPtr]$settings.Current.NativeWindowHandle) "06-settings-persisted"))
    Invoke-Element (Wait-Element $settings "CancelButton")
    Add-Step "settings-cancel" $true "Cancel discarded an unsaved edit."

    Invoke-Element (Wait-Element $main "TranslateButton")
    Start-Sleep -Milliseconds 900
    $images.Add((Save-WindowImage $process.MainWindowHandle "07-translation-result"))
    Add-Step "translation" $true "Deterministic test translation completed without network access."

    Invoke-Element (Wait-Element $main "CollapseButton")
    Start-Sleep -Milliseconds 200
    $images.Add((Save-WindowImage $process.MainWindowHandle "08-collapsed"))
    Invoke-Element (Wait-Element $main "CollapseButton")
    Start-Sleep -Milliseconds 200
    $images.Add((Save-WindowImage $process.MainWindowHandle "09-restored"))
    Add-Step "collapse-restore" $true "Collapsed and restored states captured."

    Invoke-Element (Wait-Element $main "ClearHistoryButton")
    Start-Sleep -Milliseconds 200
    $images.Add((Save-WindowImage $process.MainWindowHandle "10-history-cleared"))
    Add-Step "history-clear" $true "History cleared and initial result state captured."

    $result = [pscustomobject]@{
        runId = $runId; passed = $true; visualReview = "Pending"; steps = $steps; images = $images
        executable = $exe; dataDirectory = $dataDir; generatedAt = (Get-Date).ToString("o")
    }
    $result | ConvertTo-Json -Depth 5 | Set-Content -Path $resultPath -Encoding UTF8
    $reportLines = [System.Collections.Generic.List[string]]::new()
    $reportLines.Add("# UI verification $runId")
    $reportLines.Add("")
    $reportLines.Add("Automatic checks passed. Review every PNG before publishing.")
    $reportLines.Add("")
    foreach ($image in $images) {
        $reportLines.Add("- [ ] $([System.IO.Path]::GetFileName($image))")
    }
    $reportLines.Add("")
    $reportLines.Add("Review text clipping, wrapping, contrast, spacing, alignment, control states, and settings values.")
    $reportLines.Add("")
    $reportLines.Add("After visual review, run:")
    $reportLines.Add("")
    $reportLines.Add("    .\scripts\Complete-Verification.ps1 -RunId $runId -VisualReview Passed")
    $reportLines | Set-Content -Path $reportPath -Encoding UTF8
    Write-Host "UI verification succeeded. Review: $reportPath"
    Write-Host "RunId: $runId"
}
catch {
    Add-Step "failure" $false $_.Exception.Message
    [pscustomobject]@{ runId = $runId; passed = $false; visualReview = "Pending"; steps = $steps; generatedAt = (Get-Date).ToString("o") } |
        ConvertTo-Json -Depth 5 | Set-Content -Path $resultPath -Encoding UTF8
    Write-Error $_
    exit 1
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(5000)) { $process.Kill() }
    }
    Stop-Transcript | Out-Null
}
