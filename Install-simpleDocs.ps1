param(
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"

$packageDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceExe = Join-Path $packageDirectory "CodeExplainer.exe"
$sourceConfig = Join-Path $packageDirectory "appsettings.json"
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    $sourceExe = Join-Path $packageDirectory "app\CodeExplainer.exe"
}
if (-not (Test-Path -LiteralPath $sourceConfig -PathType Leaf)) {
    $sourceConfig = Join-Path $packageDirectory "app\appsettings.json"
}
$installDirectory = Join-Path $env:LOCALAPPDATA "Programs\simpleDocs"
$installedExe = Join-Path $installDirectory "CodeExplainer.exe"
$temporaryExe = Join-Path $installDirectory "CodeExplainer.installing.exe"
$logDirectory = Join-Path $env:LOCALAPPDATA "simpleDocs"
$logPath = Join-Path $logDirectory "install.log"

if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    throw "CodeExplainer.exe must be in the same extracted folder as this installer."
}
if (-not (Test-Path -LiteralPath $sourceConfig -PathType Leaf)) {
    throw "appsettings.json must be in the same extracted folder as this installer."
}

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null

$installedPath = [IO.Path]::GetFullPath($installedExe)
$runningInstalledCopies = @(Get-CimInstance Win32_Process -Filter "Name='CodeExplainer.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.ExecutablePath -and [IO.Path]::GetFullPath($_.ExecutablePath).Equals($installedPath, [StringComparison]::OrdinalIgnoreCase) })

foreach ($running in $runningInstalledCopies) {
    $process = Get-Process -Id $running.ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) { continue }
    [void]$process.CloseMainWindow()
    if (-not $process.WaitForExit(3000)) {
        Stop-Process -Id $process.Id -Force
        [void]$process.WaitForExit(3000)
    }
}

Copy-Item -LiteralPath $sourceExe -Destination $temporaryExe -Force
if (Test-Path -LiteralPath $installedExe) {
    [IO.File]::Delete($installedExe)
}
[IO.File]::Move($temporaryExe, $installedExe)
Copy-Item -LiteralPath $sourceConfig -Destination (Join-Path $installDirectory "appsettings.json") -Force

$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
New-Item -Path $runKey -Force | Out-Null
New-ItemProperty -Path $runKey -Name "simpleDocs" -Value ('"' + $installedExe + '"') -PropertyType String -Force | Out-Null

$preferencesKey = "HKCU:\Software\simpleDocs"
New-Item -Path $preferencesKey -Force | Out-Null
New-ItemProperty -Path $preferencesKey -Name "StartWithWindows" -Value 1 -PropertyType DWord -Force | Out-Null

$programsDirectory = [Environment]::GetFolderPath("Programs")
$shortcutPath = Join-Path $programsDirectory "simpleDocs.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $installedExe
$shortcut.WorkingDirectory = $installDirectory
$shortcut.Description = "simpleDocs contextual AI assistant"
$shortcut.Save()

$installedVersion = (Get-Item -LiteralPath $installedExe).VersionInfo.ProductVersion
"$(Get-Date -Format o) Installed simpleDocs $installedVersion to $installedExe" | Add-Content -LiteralPath $logPath -Encoding UTF8
Write-Host "simpleDocs $installedVersion was installed successfully."
Write-Host "It will start automatically after Windows sign-in."

if (-not $NoLaunch) {
    Start-Process -FilePath $installedExe -WorkingDirectory $installDirectory -WindowStyle Hidden
}
