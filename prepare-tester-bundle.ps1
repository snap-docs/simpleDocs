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
New-Item -ItemType Directory -Path (Join-Path $outputDir "app") | Out-Null
New-Item -ItemType Directory -Path (Join-Path $outputDir "docs") | Out-Null
New-Item -ItemType Directory -Path (Join-Path $outputDir "editor-extension") | Out-Null

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

    Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $outputDir "app" $fileName) -Force
}

if (Test-Path (Join-Path $projectRoot "final-tester-package-guide.md")) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "final-tester-package-guide.md") -Destination (Join-Path $outputDir "docs\final-tester-package-guide.md") -Force
}
if (Test-Path (Join-Path $projectRoot "chatgpt-tester-plan-prompt.md")) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "chatgpt-tester-plan-prompt.md") -Destination (Join-Path $outputDir "docs\chatgpt-tester-plan-prompt.md") -Force
}

foreach ($manualName in @("simpleDocs-user-manual.pdf", "simpleDocs-user-manual.txt")) {
    $manualPath = Join-Path $projectRoot "release-assets\$manualName"
    if (-not (Test-Path $manualPath)) {
        throw "Required user manual is missing: $manualPath"
    }
    Copy-Item -LiteralPath $manualPath -Destination (Join-Path $outputDir "docs\$manualName") -Force
}

$extensionPackage = Get-ChildItem -LiteralPath (Join-Path $projectRoot "dist") -Filter "simpledocs-context-*.vsix" -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
if ($extensionPackage) {
    Copy-Item -LiteralPath $extensionPackage.FullName -Destination (Join-Path $outputDir "editor-extension" $extensionPackage.Name) -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot "editor-extension\README.md") -Destination (Join-Path $outputDir "editor-extension\README.md") -Force
}

$readmePath = Join-Path $outputDir "README-FIRST.txt"
$readme = @"
simpleDocs tester bundle

1. Extract this zip first
2. Open the app folder
3. Run CodeExplainer.exe
4. No account or redeem code is required
5. Use the configured hotkey inside your normal workflow
6. The app starts with Windows by default and can be changed from the tray menu
7. VS Code or Cursor users can install the optional VSIX in editor-extension for exact unsaved-buffer context

Environment: $EnvironmentName
"@
Set-Content -LiteralPath $readmePath -Value $readme -Encoding ASCII

Write-Host "Tester bundle prepared at $outputDir"
