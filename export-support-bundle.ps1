param(
    [string]$OutputRoot = ".\dist\support"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputDir = Join-Path $projectRoot $OutputRoot
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$bundleDir = Join-Path $outputDir "support_$timestamp"
$zipPath = Join-Path $outputDir "support_$timestamp.zip"

if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

if (Test-Path $bundleDir) {
    Remove-Item -LiteralPath $bundleDir -Recurse -Force
}

New-Item -ItemType Directory -Path $bundleDir | Out-Null

$pathsToCopy = @(
    "runlogs",
    "client\appsettings.json",
    "client\appsettings.Staging.json",
    "client\appsettings.Production.json",
    "release-checklist.md",
    "launch-docs.md"
)

foreach ($relativePath in $pathsToCopy) {
    $sourcePath = Join-Path $projectRoot $relativePath
    if (Test-Path $sourcePath) {
        $destinationPath = Join-Path $bundleDir $relativePath
        $destinationParent = Split-Path -Parent $destinationPath
        if (-not (Test-Path $destinationParent)) {
            New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
        }

        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Recurse -Force
    }
}

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $bundleDir "*") -DestinationPath $zipPath -Force
Write-Host "Support bundle exported to $zipPath"
