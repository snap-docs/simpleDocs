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

$clientConfigPath = Join-Path $clientDistPath "appsettings.json"
if (-not (Test-Path $clientConfigPath)) {
    throw "Client appsettings.json was not found: $clientConfigPath"
}

$clientConfig = Get-Content -LiteralPath $clientConfigPath -Raw | ConvertFrom-Json
if ($EnvironmentName -eq "Production") {
    if ($clientConfig.Environment -ne "Production" -or
        $clientConfig.Auth.Enabled -ne $false -or
        $clientConfig.Backend.ApiBaseUrl -notmatch '^https://' -or
        $clientConfig.Backend.WsBaseUrl -notmatch '^wss://') {
        throw "Refusing to create a Production tester bundle from non-production client configuration."
    }
}

if (Test-Path $outputDir) {
    Remove-Item -LiteralPath $outputDir -Recurse -Force
}

New-Item -ItemType Directory -Path $outputDir | Out-Null

foreach ($installerName in @("Install-simpleDocs.cmd", "Install-simpleDocs.ps1")) {
    $installerPath = Join-Path $projectRoot $installerName
    if (-not (Test-Path $installerPath)) {
        throw "Required installer file not found: $installerPath"
    }
    Copy-Item -LiteralPath $installerPath -Destination (Join-Path $outputDir $installerName) -Force
}

# Copy only the files testers actually need to run the app.
$runtimeFiles = @(
    "CodeExplainer.exe",
    "appsettings.json"
)

foreach ($fileName in $runtimeFiles) {
    $sourcePath = Join-Path $clientDistPath $fileName
    if (-not (Test-Path $sourcePath)) {
        throw "Required client file not found: $sourcePath"
    }

    Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $outputDir $fileName) -Force
}

foreach ($manualName in @("simpleDocs-user-manual.pdf", "simpleDocs-user-manual.txt")) {
    $manualPath = Join-Path $projectRoot "release-assets\$manualName"
    if (-not (Test-Path $manualPath)) {
        throw "Required user manual is missing: $manualPath"
    }
    Copy-Item -LiteralPath $manualPath -Destination (Join-Path $outputDir $manualName) -Force
}

Write-Host "Tester bundle prepared at $outputDir"
