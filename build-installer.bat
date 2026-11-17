@echo off
setlocal
cd /d "%~dp0"

echo ==============================================
echo       SQLitePass - Compilazione Installer
echo ==============================================
echo.

set VERSION=1.1.2
if not "%~1"=="" set VERSION=%~1

powershell -ExecutionPolicy Bypass -File ".\build-installer.ps1" -Version "%VERSION%"
if errorlevel 1 (
    echo Compilazione interrotta. Consultare l'errore riportato sopra.
    exit /b 1
)

echo.
echo Operazione completata. I file si trovano nella cartella artifacts\releases\
pause
