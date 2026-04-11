param(
    [string]$ClientDist = ".\dist\client",
    [string]$OutputRoot = ".\dist\tester-bundle",
    [string]$EnvironmentName = "Production"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$clientDistPath = Join-Path $projectRoot $ClientDist
$outputDir = Join-Path $projectRoot $OutputRoot

if (-not (Test-Path $clientDistPath)) {
    throw "Client dist not found. Run publish-client.ps1 first."
}

if (Test-Path $outputDir) {
    Remove-Item -LiteralPath $outputDir -Recurse -Force
}

New-Item -ItemType Directory -Path $outputDir | Out-Null
New-Item -ItemType Directory -Path (Join-Path $outputDir "app") | Out-Null
New-Item -ItemType Directory -Path (Join-Path $outputDir "docs") | Out-Null

Copy-Item -Path (Join-Path $clientDistPath "*") -Destination (Join-Path $outputDir "app") -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot "release-checklist.md") -Destination (Join-Path $outputDir "docs\release-checklist.md") -Force
Copy-Item -LiteralPath (Join-Path $projectRoot "launch-docs.md") -Destination (Join-Path $outputDir "docs\launch-docs.md") -Force
if (Test-Path (Join-Path $projectRoot "pilot-user-guide.md")) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "pilot-user-guide.md") -Destination (Join-Path $outputDir "docs\pilot-user-guide.md") -Force
}

$readmePath = Join-Path $outputDir "README-FIRST.txt"
$readme = @"
simpleDocs tester bundle

1. Open the app folder
2. Run Start-CodeExplainer.bat
3. Enter the redeem code you received
4. Use the configured hotkey inside your normal workflow

Environment: $EnvironmentName
"@
Set-Content -LiteralPath $readmePath -Value $readme -Encoding ASCII

Write-Host "Tester bundle prepared at $outputDir"
