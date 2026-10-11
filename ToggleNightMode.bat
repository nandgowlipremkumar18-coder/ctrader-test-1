@echo off
:: Toggle Night Mode (0% Brightness + Dark Theme) and Day Mode (100% Brightness + Light Theme)
powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0NightModeToggle.ps1"
