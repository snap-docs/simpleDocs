@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-simpleDocs.ps1"
if errorlevel 1 (
  echo.
  echo simpleDocs installation did not complete. Review the message above and try again.
  pause
  exit /b 1
)
exit /b 0
