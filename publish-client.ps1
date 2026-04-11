param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$EnvironmentName = "Production",
    [string]$OutputRoot = ".\dist\client",
    [string]$Version = "1.1.0-pilot",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$clientProject = Join-Path $projectRoot "client\CodeExplainer.csproj"
$publishDir = Join-Path $projectRoot $OutputRoot

if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

Write-Host "Restoring client for runtime $Runtime ..."
dotnet restore $clientProject -r $Runtime
if ($LASTEXITCODE -ne 0) {
    throw "Client restore failed with exit code $LASTEXITCODE."
}

$publishArgs = @(
    "publish", $clientProject,
    "-c", $Configuration,
    "-r", $Runtime,
    "-o", $publishDir,
    "/p:Version=$Version",
    "/p:InformationalVersion=$Version",
    "/p:PublishSingleFile=true",
    "/p:IncludeNativeLibrariesForSelfExtract=true"
)

if ($SelfContained.IsPresent) {
    $publishArgs += "--self-contained"
    $publishArgs += "true"
} else {
    $publishArgs += "--self-contained"
    $publishArgs += "false"
}

Write-Host "Publishing client to $publishDir ..."
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "Client publish failed with exit code $LASTEXITCODE."
}

$publishedBaseConfigPath = Join-Path $publishDir "appsettings.json"
$publishedEnvironmentConfigPath = Join-Path $publishDir "appsettings.$EnvironmentName.json"
if (Test-Path $publishedEnvironmentConfigPath) {
    Copy-Item -LiteralPath $publishedEnvironmentConfigPath -Destination $publishedBaseConfigPath -Force
}

$launcherPath = Join-Path $publishDir "Start-CodeExplainer.bat"
$launcherContent = @"
@echo off
set CODE_EXPLAINER_ENV=$EnvironmentName
start "" "%~dp0CodeExplainer.exe"
"@
Set-Content -LiteralPath $launcherPath -Value $launcherContent -Encoding ASCII

$manifestPath = Join-Path $publishDir "release-manifest.json"
$manifest = @{
    product = "simpleDocs"
    version = $Version
    environment = $EnvironmentName
    runtime = $Runtime
    configuration = $Configuration
    generated_at_utc = [DateTime]::UtcNow.ToString("o")
} | ConvertTo-Json
Set-Content -LiteralPath $manifestPath -Value $manifest -Encoding ASCII

Write-Host "Client publish complete."
Write-Host "Default EXE environment: $EnvironmentName"
Write-Host "Environment launcher: $launcherPath"
