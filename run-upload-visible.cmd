@echo off
title NyxNova GitHub Release Upload
cd /d "%~dp0"
echo NyxNova Upload-Fenster
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0upload-nyxnova-installer.ps1"
echo.
echo Fenster bleibt offen. Zum Schliessen eine Taste druecken.
pause >nul
