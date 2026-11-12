@echo off
setlocal
cd /d "%~dp0"

echo ==============================================
echo       SQLitePass - Compilazione Installer
echo ==============================================
echo.

set VERSION=1.0.2
if not "%~1"=="" set VERSION=%~1

powershell -ExecutionPolicy Bypass -File ".\build-installer.ps1" -Version "%VERSION%"

echo.
echo Operazione completata. I file si trovano nella cartella \releases\
pause
