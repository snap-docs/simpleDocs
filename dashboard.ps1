# Code Explainer - Live Capture Dashboard
# Watches client_live.log and prints a formatted summary of every capture event.

$logFile = "D:\PROJECTS\Startup\prototype4\runlogs\client_live.log"

$state = @{}
$lastSeenLines = 0

function Write-Divider {
    Write-Host ("-" * 72) -ForegroundColor DarkGray
}

function Write-Row {
    param($label, $value, $color = "Yellow")
    Write-Host ("  {0,-26}" -f ($label + ":")) -ForegroundColor White -NoNewline
    Write-Host $value -ForegroundColor $color
}

function Process-Line($line) {
    if ($line -match "req=(\d+)") { $reqId = $Matches[1] } else { $reqId = "0" }

    # Hotkey triggered - start new request block
    if ($line -match "stage=hotkey_triggered") {
        $key = if ($line -match 'key="([^"]+)"') { $Matches[1] } else { "unknown" }
        $ts  = if ($line -match '^\[([^\]]+)\]') { $Matches[1] } else { "" }
        $state[$reqId] = @{
            req            = $reqId
            ts             = $ts
            key            = $key
            process        = ""
            title          = ""
            env            = ""
            selMethod      = ""
            bgMethod       = ""
            selChars       = 0
            bgChars        = 0
            selPreview     = ""
            bgPreview      = ""
            ocrUsed        = $false
            ocrConf        = ""
            durationCapture = ""
            durationStream  = ""
            warnings       = [System.Collections.Generic.List[string]]::new()
            isPartial      = $false
            isUnsupported  = $false
        }
        Write-Host ""
        Write-Divider
        Write-Host ("  >> HOTKEY PRESSED   req=" + $reqId + "   " + $ts) -ForegroundColor Cyan
        Write-Divider
        return
    }

    if (-not $state.ContainsKey($reqId)) { return }
    $s = $state[$reqId]

    # App + title
    if ($line -match "process=(\S+)") { $s.process = $Matches[1] }
    if ($line -match 'title="([^"]+)"') { $s.title = $Matches[1] }

    # Capture methods
    if ($line -match "selected_method=(\S+)") { $s.selMethod = $Matches[1] }
    if ($line -match "background_method=(\S+)") { $s.bgMethod = $Matches[1] }

    # Environment
    if ($line -match "env=(\S+)") { $s.env = $Matches[1] }

    # Char counts
    if ($line -match "selected_chars=(\d+)") { $s.selChars = [int]$Matches[1] }
    if ($line -match "background_chars=(\d+)") { $s.bgChars = [int]$Matches[1] }

    # Partial / unsupported flags
    if ($line -match "is_partial=True") { $s.isPartial = $true }
    if ($line -match "is_unsupported=True") { $s.isUnsupported = $true }

    # Previews
    if ($line -match "Selected preview: (.+)$") { $s.selPreview = $Matches[1] }
    if ($line -match "Background preview: (.+)$") { $s.bgPreview = $Matches[1] }

    # OCR
    if ($line -match "OCR.*confidence=([\d.]+)") {
        $s.ocrUsed = $true
        $s.ocrConf = $Matches[1]
    }
    if ($line -match "OCR (fallback|background|selected|medium)" -and -not $s.ocrUsed) {
        $s.ocrUsed = $true
    }

    # Warnings
    if ($line -match "\] WARN ") {
        $msg = $line -replace '^\[[^\]]+\] WARN  \[[^\]]+\] ', ""
        if ($msg.Length -lt 140) { $null = $s.warnings.Add($msg) }
    }

    # Capture finished
    if ($line -match "stage=hotkey_finished.*duration_ms=(\d+)") {
        $s.durationCapture = $Matches[1] + " ms"
    }

    # Stream finished - print full summary
    if ($line -match "stage=stream_finished.*duration_ms=(\d+)") {
        $s.durationStream = $Matches[1] + " ms"
        Print-Summary $s
        $state.Remove($reqId)
    }
}

function Print-Summary($s) {
    # App
    Write-Row "App/Process"     ($s.process + "   " + $s.title) Green

    # Environment
    $envLabel = $s.env
    $envColor = "Green"
    if ($s.isUnsupported) { $envLabel += "  [UNSUPPORTED]"; $envColor = "Red" }
    elseif ($s.isPartial) { $envLabel += "  [partial]";     $envColor = "Yellow" }
    Write-Row "Environment"     $envLabel $envColor

    # Selected text
    if ($s.selChars -gt 0) {
        Write-Row "Selected Text"   ("OK - " + $s.selChars + " chars   [" + $s.selMethod + "]") Green
        if ($s.selPreview) {
            Write-Row "  Preview"    ('"' + $s.selPreview + '"') DarkGray
        }
    } else {
        Write-Row "Selected Text"   ("NONE   [" + $s.selMethod + "]") Red
    }

    # Background text
    if ($s.bgChars -gt 0) {
        Write-Row "Background Text" ("OK - " + $s.bgChars + " chars   [" + $s.bgMethod + "]") Green
        if ($s.bgPreview) {
            Write-Row "  Preview"    ('"' + $s.bgPreview + '"') DarkGray
        }
    } else {
        Write-Row "Background Text" ("NONE   [" + $s.bgMethod + "]") Yellow
    }

    # OCR
    if ($s.ocrUsed) {
        $ocrStr = "YES"
        if ($s.ocrConf) { $ocrStr += "   confidence=" + $s.ocrConf }
        Write-Row "OCR Fallback"    $ocrStr Magenta
    } else {
        Write-Row "OCR Fallback"    "not needed" DarkGray
    }

    # Timing
    $timeStr = ""
    if ($s.durationCapture) { $timeStr += "capture=" + $s.durationCapture + "   " }
    if ($s.durationStream)  { $timeStr += "stream="  + $s.durationStream }
    Write-Row "Timing"           $timeStr White

    # Warnings
    if ($s.warnings.Count -gt 0) {
        Write-Host "  Warnings:" -ForegroundColor Yellow
        foreach ($w in $s.warnings) {
            Write-Host ("    ! " + $w) -ForegroundColor Yellow
        }
    }

    Write-Divider
}

# --- Main loop ---
Clear-Host
Write-Host ""
Write-Host "  Code Explainer - Live Capture Dashboard" -ForegroundColor Cyan
Write-Host ("  Log: " + $logFile) -ForegroundColor DarkGray
Write-Host "  Press Ctrl+C to stop." -ForegroundColor DarkGray
Write-Divider
Write-Host "  Waiting for hotkey press (Ctrl+Shift+Space)..." -ForegroundColor DarkGray

if (-not (Test-Path $logFile)) {
    Write-Host "  ERROR: Log file not found. Start the app first with .\run.ps1" -ForegroundColor Red
    exit 1
}

$lastSeenLines = @(Get-Content $logFile).Count

while ($true) {
    Start-Sleep -Milliseconds 250

    $allLines  = @(Get-Content $logFile)
    $newLines  = $allLines | Select-Object -Skip $lastSeenLines
    $lastSeenLines = $allLines.Count

    foreach ($line in $newLines) {
        if ($line.Trim()) { Process-Line $line }
    }
}
