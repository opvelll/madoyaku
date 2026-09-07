param(
    [Parameter(Mandatory = $true)][string]$RunId,
    [Parameter(Mandatory = $true)][ValidateSet("Passed")][string]$VisualReview
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifactDir = Join-Path $root "artifacts\ui-verification\$RunId"
$resultPath = Join-Path $artifactDir "result.json"
$reportPath = Join-Path $artifactDir "report.md"
$finalPublish = Join-Path $root "publish"

if (-not (Test-Path $resultPath)) { throw "Verification result not found: $resultPath" }
$result = Get-Content $resultPath -Raw | ConvertFrom-Json
if (-not $result.passed) { throw "Automatic UI verification did not pass." }
if (-not $result.images -or $result.images.Count -lt 1) { throw "Verification images are missing." }
foreach ($image in $result.images) {
    if (-not (Test-Path $image)) { throw "Verification image is missing: $image" }
}

& dotnet build (Join-Path $root "Madoyaku.csproj") -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Release build failed." }
& dotnet format (Join-Path $root "Madoyaku.csproj") --verify-no-changes --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet format verification failed." }
& git -C $root diff --check
if ($LASTEXITCODE -ne 0) { throw "git diff --check failed." }

$smokeData = Join-Path $artifactDir "publish-smoke-data"
& (Join-Path $root "scripts\Publish-App.ps1") -OutputRoot $finalPublish -SkipValidation -SmokeDataDirectory $smokeData
if ($LASTEXITCODE -ne 0) { throw "Standalone publish failed." }
$publishedExe = Join-Path $finalPublish "Madoyaku\Madoyaku.exe"
$publishedZip = Join-Path $finalPublish "Madoyaku-win-x64.zip"
if (-not (Test-Path $publishedExe)) { throw "Published executable was not produced." }
if (-not (Test-Path $publishedZip)) { throw "Published ZIP was not produced." }
$result.visualReview = $VisualReview
$result | Add-Member -NotePropertyName completedAt -NotePropertyValue (Get-Date).ToString("o") -Force
$result | Add-Member -NotePropertyName publishedExecutable -NotePropertyValue $publishedExe -Force
$result | Add-Member -NotePropertyName publishedZip -NotePropertyValue $publishedZip -Force
$result | ConvertTo-Json -Depth 6 | Set-Content -Path $resultPath -Encoding UTF8
Add-Content -Path $reportPath -Value "`nVisual review: Passed`nPublished executable updated: publish\Madoyaku\Madoyaku.exe`nPublished ZIP updated: publish\Madoyaku-win-x64.zip"
Write-Host "Verification complete. publish\Madoyaku\Madoyaku.exe and publish\Madoyaku-win-x64.zip were regenerated."
