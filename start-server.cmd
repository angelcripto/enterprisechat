@echo off
setlocal
cd /d "%~dp0"
rem No fijamos ASPNETCORE_ENVIRONMENT a propósito: el launcher interactivo
rem (InteractiveLauncher.cs) pregunta el modo y crea las claves si faltan.
rem Para saltarte el prompt: set ASPNETCORE_ENVIRONMENT=Development antes,
rem o ejecuta con --no-interactive.
dotnet run --project src\EnterpriseChat.Server --no-build %*
endlocal
