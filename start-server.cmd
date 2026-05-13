@echo off
setlocal
cd /d "%~dp0"
set ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src\EnterpriseChat.Server --no-build
endlocal
