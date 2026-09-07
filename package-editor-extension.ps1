param([string]$OutputRoot = ".\dist")

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$extensionRoot = Join-Path $projectRoot "editor-extension"
$outputDir = Join-Path $projectRoot $OutputRoot
$package = Get-Content -LiteralPath (Join-Path $extensionRoot "package.json") -Raw | ConvertFrom-Json
$outputPath = Join-Path $outputDir "$($package.name)-$($package.version).vsix"

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
Push-Location $extensionRoot
try {
    npx --yes '@vscode/vsce' package --no-dependencies --out $outputPath
    if ($LASTEXITCODE -ne 0) { throw "Editor extension packaging failed with exit code $LASTEXITCODE." }
}
finally { Pop-Location }

Write-Host "Editor extension package created at $outputPath"
