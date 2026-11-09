@echo off
rem Double-click to install or update iCUE-HA-Bridge (asks for administrator rights).
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
