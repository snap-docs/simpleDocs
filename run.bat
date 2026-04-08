@echo off
echo Starting Prototype4 Backend and Client...

cd backend
start "Prototype4 Backend" cmd /k "npm run dev"

cd ..\client
start "Prototype4 Client" cmd /k "dotnet run"

cd ..
echo Services starting in separate windows.
