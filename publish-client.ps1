param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$EnvironmentName = "Production",
    [string]$OutputRoot = ".\dist\client",
    [string]$Version = "1.4.3-pilot",
    [switch]$SelfContained,
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$clientProject = Join-Path $projectRoot "client\CodeExplainer.csproj"
$publishDir = Join-Path $projectRoot $OutputRoot
$publishSelfContained = -not $FrameworkDependent.IsPresent

if ($SelfContained.IsPresent -and $FrameworkDependent.IsPresent) {
    throw "Choose either -SelfContained or -FrameworkDependent, not both."
}

if ($SelfContained.IsPresent) {
    $publishSelfContained = $true
}

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

if ($publishSelfContained) {
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

if (-not (Test-Path $publishedBaseConfigPath)) {
    throw "Published appsettings.json was not found."
}

$publishedConfig = Get-Content -LiteralPath $publishedBaseConfigPath -Raw | ConvertFrom-Json
if ($EnvironmentName -eq "Production") {
    if ($publishedConfig.Environment -ne "Production") {
        throw "Production package validation failed: Environment must be Production."
    }

    if ($publishedConfig.Auth.Enabled -ne $false) {
        throw "Production package validation failed: this distribution must run without sign-in."
    }

    if ($publishedConfig.Backend.ApiBaseUrl -notmatch '^https://') {
        throw "Production package validation failed: ApiBaseUrl must use HTTPS."
    }

    if ($publishedConfig.Backend.WsBaseUrl -notmatch '^wss://') {
        throw "Production package validation failed: WsBaseUrl must use WSS."
    }
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
    self_contained = $publishSelfContained
    generated_at_utc = [DateTime]::UtcNow.ToString("o")
} | ConvertTo-Json
Set-Content -LiteralPath $manifestPath -Value $manifest -Encoding ASCII

Write-Host "Client publish complete."
Write-Host "Default EXE environment: $EnvironmentName"
Write-Host "Environment launcher: $launcherPath"
