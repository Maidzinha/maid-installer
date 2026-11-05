@echo off
rem Instala o Maid Installer para o usuario atual (sem administrador).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Instalar.ps1"
pause
