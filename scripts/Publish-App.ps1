param(
    [string]$OutputRoot,
    [switch]$SkipValidation,
    [string]$SmokeDataDirectory
)

$ErrorActionPreference = "Stop"

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$project = Join-Path $root "Madoyaku.csproj"
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $root "publish"
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Project file not found: $project"
}
if (Test-Path -LiteralPath $OutputRoot -PathType Leaf) {
    throw "Output path is a file: $OutputRoot"
}

$runId = Get-Date -Format "yyyyMMdd-HHmmss"
$stageRoot = Join-Path $root "artifacts\publish-staging\$runId"
$stagePackage = Join-Path $stageRoot "Madoyaku"
$stageZip = Join-Path $stageRoot "Madoyaku-win-x64.zip"
$finalPackage = Join-Path $OutputRoot "Madoyaku"
$finalZip = Join-Path $OutputRoot "Madoyaku-win-x64.zip"
$commitRoot = Join-Path $OutputRoot ".Madoyaku-next-$runId"
$backupPackage = Join-Path $OutputRoot ".Madoyaku-backup-$runId"
$backupZip = Join-Path $OutputRoot ".Madoyaku-win-x64-backup-$runId.zip"

function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE."
    }
}

function Remove-ExactPath([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Test-PublishedWindow([string]$Executable, [string]$DataDirectory) {
    New-Item -ItemType Directory -Force -Path $DataDirectory | Out-Null
    $process = Start-Process -FilePath $Executable -WorkingDirectory (Split-Path -Parent $Executable) `
        -ArgumentList @("--ui-test", "--test-data-dir", $DataDirectory) -PassThru
    try {
        $deadline = (Get-Date).AddSeconds(15)
        do {
            if ($process.HasExited) {
                throw "Published executable exited before creating a window (exit code $($process.ExitCode))."
            }
            $process.Refresh()
            if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
                return
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        throw "Published executable did not create a window within 15 seconds."
    }
    finally {
        if ($null -ne $process -and -not $process.HasExited) {
            $process.CloseMainWindow() | Out-Null
            if (-not $process.WaitForExit(5000)) {
                $process.Kill()
            }
        }
    }
}

try {
    Remove-ExactPath $stageRoot
    New-Item -ItemType Directory -Force -Path $stagePackage | Out-Null

    if (-not $SkipValidation) {
        Invoke-Checked "dotnet" @("build", $project, "-c", "Release", "--nologo")
        Invoke-Checked "dotnet" @("format", $project, "--verify-no-changes", "--no-restore")
        Invoke-Checked "git" @("-C", $root, "diff", "--check")
    }

    $publishArguments = @(
        "publish", $project,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:DebugSymbols=false",
        "-p:DebugType=None",
        "-o", $stageRoot
    )
    Invoke-Checked "dotnet" $publishArguments

    $publishedFiles = Get-ChildItem -LiteralPath $stageRoot -File -Recurse |
        Where-Object { $_.Extension -ne ".pdb" }
    foreach ($file in $publishedFiles) {
        $relativePath = $file.FullName.Substring($stageRoot.Length).TrimStart([char[]]@("\", "/"))
        $destination = Join-Path $stagePackage $relativePath
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Move-Item -LiteralPath $file.FullName -Destination $destination -Force
    }

    $publishedExe = Join-Path $stagePackage "Madoyaku.exe"
    if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
        throw "Published executable was not produced: $publishedExe"
    }

    Copy-Item -LiteralPath (Join-Path $root "LICENSE") -Destination (Join-Path $stagePackage "LICENSE")
    Copy-Item -LiteralPath (Join-Path $root "はじめに.txt") -Destination (Join-Path $stagePackage "はじめに.txt")

    if ([string]::IsNullOrWhiteSpace($SmokeDataDirectory)) {
        $SmokeDataDirectory = Join-Path $stageRoot "smoke-data"
    }
    Test-PublishedWindow $publishedExe ([System.IO.Path]::GetFullPath($SmokeDataDirectory))

    Compress-Archive -Path $stagePackage -DestinationPath $stageZip -CompressionLevel Optimal

    New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
    Remove-ExactPath $commitRoot
    New-Item -ItemType Directory -Force -Path $commitRoot | Out-Null
    Copy-Item -LiteralPath $stagePackage -Destination (Join-Path $commitRoot "Madoyaku") -Recurse
    Copy-Item -LiteralPath $stageZip -Destination (Join-Path $commitRoot "Madoyaku-win-x64.zip")

    $movedPackageBackup = $false
    $movedZipBackup = $false
    $newPackageMoved = $false
    $newZipMoved = $false
    $committed = $false
    try {
        Remove-ExactPath $backupPackage
        Remove-ExactPath $backupZip
        if (Test-Path -LiteralPath $finalPackage) {
            Move-Item -LiteralPath $finalPackage -Destination $backupPackage
            $movedPackageBackup = $true
        }
        if (Test-Path -LiteralPath $finalZip) {
            Move-Item -LiteralPath $finalZip -Destination $backupZip
            $movedZipBackup = $true
        }
        Move-Item -LiteralPath (Join-Path $commitRoot "Madoyaku") -Destination $finalPackage
        $newPackageMoved = $true
        Move-Item -LiteralPath (Join-Path $commitRoot "Madoyaku-win-x64.zip") -Destination $finalZip
        $newZipMoved = $true
        $committed = $true
    }
    finally {
        if (-not $committed) {
            if ($newPackageMoved) { Remove-ExactPath $finalPackage }
            if ($newZipMoved) { Remove-ExactPath $finalZip }
            if ($movedPackageBackup) { Move-Item -LiteralPath $backupPackage -Destination $finalPackage }
            if ($movedZipBackup) { Move-Item -LiteralPath $backupZip -Destination $finalZip }
        }
        else {
            Remove-ExactPath $backupPackage
            Remove-ExactPath $backupZip
        }
    }

    $legacyRootFiles = @(
        "HonnyakuKun.exe",
        "HonnyakuKun.pdb",
        "Madoyaku.exe",
        "Madoyaku.pdb",
        "D3DCompiler_47_cor3.dll",
        "PenImc_cor3.dll",
        "PresentationNative_cor3.dll",
        "vcruntime140_cor3.dll",
        "wpfgfx_cor3.dll"
    )
    foreach ($legacyFile in $legacyRootFiles) {
        Remove-ExactPath (Join-Path $OutputRoot $legacyFile)
    }

    Write-Host "Published directory: $finalPackage"
    Write-Host "Published ZIP: $finalZip"
}
finally {
    Remove-ExactPath $commitRoot
    Remove-ExactPath $stageRoot
}
