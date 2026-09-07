param(
    [Parameter(Mandatory = $true)][string]$RunId,
    [Parameter(Mandatory = $true)][ValidateSet("Passed")][string]$VisualReview
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifactDir = Join-Path $root "artifacts\ui-verification\$RunId"
$resultPath = Join-Path $artifactDir "result.json"
$reportPath = Join-Path $artifactDir "report.md"
$temporaryPublish = Join-Path $artifactDir "publish"
$finalPublish = Join-Path $root "publish"

if (-not (Test-Path $resultPath)) { throw "Verification result not found: $resultPath" }
$result = Get-Content $resultPath -Raw | ConvertFrom-Json
if (-not $result.passed) { throw "Automatic UI verification did not pass." }
if (-not $result.images -or $result.images.Count -lt 1) { throw "Verification images are missing." }
foreach ($image in $result.images) {
    if (-not (Test-Path $image)) { throw "Verification image is missing: $image" }
}

& dotnet build (Join-Path $root "HonnyakuKun.csproj") -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Release build failed." }
& dotnet format (Join-Path $root "HonnyakuKun.csproj") --verify-no-changes --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet format verification failed." }
& git -C $root diff --check
if ($LASTEXITCODE -ne 0) { throw "git diff --check failed." }

if (Test-Path $temporaryPublish) { Remove-Item -LiteralPath $temporaryPublish -Recurse -Force }
& dotnet publish (Join-Path $root "HonnyakuKun.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $temporaryPublish
if ($LASTEXITCODE -ne 0) { throw "Standalone publish failed." }
$publishedExe = Join-Path $temporaryPublish "HonnyakuKun.exe"
if (-not (Test-Path $publishedExe)) { throw "Published executable was not produced." }

if (-not (Test-Path $finalPublish)) { New-Item -ItemType Directory -Path $finalPublish | Out-Null }
Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $finalPublish "HonnyakuKun.exe") -Force
$smokeData = Join-Path $artifactDir "publish-smoke-data"
New-Item -ItemType Directory -Force -Path $smokeData | Out-Null
$smokeProcess = Start-Process -FilePath (Join-Path $finalPublish "HonnyakuKun.exe") -ArgumentList @("--ui-test", "--test-data-dir", $smokeData) -PassThru
try {
    $smokeDeadline = (Get-Date).AddSeconds(15)
    do {
        $smokeProcess.Refresh()
        if ($smokeProcess.MainWindowHandle -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $smokeDeadline)
    if ($smokeProcess.MainWindowHandle -eq [IntPtr]::Zero) {
        throw "Published executable did not create a window."
    }
}
finally {
    if ($null -ne $smokeProcess -and -not $smokeProcess.HasExited) {
        $smokeProcess.CloseMainWindow() | Out-Null
        if (-not $smokeProcess.WaitForExit(5000)) { $smokeProcess.Kill() }
    }
}
$result.visualReview = $VisualReview
$result | Add-Member -NotePropertyName completedAt -NotePropertyValue (Get-Date).ToString("o") -Force
$result | Add-Member -NotePropertyName publishedExecutable -NotePropertyValue (Join-Path $finalPublish "HonnyakuKun.exe") -Force
$result | ConvertTo-Json -Depth 6 | Set-Content -Path $resultPath -Encoding UTF8
Add-Content -Path $reportPath -Value "`nVisual review: Passed`nPublished executable updated: publish\HonnyakuKun.exe"
Write-Host "Verification complete. publish\HonnyakuKun.exe was regenerated."
