param(
    [string]$OutputRoot = ".\dist\backend",
    [string]$Version = "1.1.0-pilot"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$backendRoot = Join-Path $projectRoot "backend"
$outputDir = Join-Path $projectRoot $OutputRoot

if (Test-Path $outputDir) {
    Remove-Item -LiteralPath $outputDir -Recurse -Force
}

New-Item -ItemType Directory -Path $outputDir | Out-Null

$copyTargets = @(
    "index.js",
    "package.json",
    "package-lock.json",
    ".env.example",
    "src",
    "db"
)

foreach ($target in $copyTargets) {
    Copy-Item -LiteralPath (Join-Path $backendRoot $target) -Destination $outputDir -Recurse -Force
}

Push-Location $outputDir
try {
    npm ci --omit=dev
    $manifest = @{
        product = "Code Explainer Backend"
        version = $Version
        generated_at_utc = [DateTime]::UtcNow.ToString("o")
    } | ConvertTo-Json
    Set-Content -LiteralPath (Join-Path $outputDir "release-manifest.json") -Value $manifest -Encoding ASCII
} finally {
    Pop-Location
}

Write-Host "Backend package prepared at $outputDir"
