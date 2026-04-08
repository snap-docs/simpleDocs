# 1) Kill anything already bound to 3000
$portPid = Get-NetTCPConnection -LocalPort 3000 -State Listen -ErrorAction SilentlyContinue |
  Select-Object -First 1 -ExpandProperty OwningProcess
if ($portPid) { Stop-Process -Id $portPid -Force }

# 2) Kill stale client process
Get-Process CodeExplainer -ErrorAction SilentlyContinue | Stop-Process -Force

# 3) Start backend in a new terminal window
Start-Process powershell -ArgumentList '-NoExit','-Command',"cd '$PSScriptRoot\backend'; npm run dev"

# 4) Start client
cd $PSScriptRoot
dotnet run --project client\CodeExplainer.csproj
