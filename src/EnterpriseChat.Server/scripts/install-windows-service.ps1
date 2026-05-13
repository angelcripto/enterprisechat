<#
.SYNOPSIS
    Installs EnterpriseChat.Server as a Windows Service.

.DESCRIPTION
    Publishes the server in Release mode (if not already), then registers it
    via sc.exe under the name 'EnterpriseChat'. Idempotent: re-running updates
    the binary path and restarts the service.

.PARAMETER PublishDir
    Where the self-contained or framework-dependent publish lives. Defaults to
    ..\publish relative to this script.

.PARAMETER ServiceName
    Windows Service name. Default 'EnterpriseChat'.

.EXAMPLE
    .\install-windows-service.ps1
#>

[CmdletBinding()]
param(
    [string]$PublishDir = (Join-Path $PSScriptRoot '..\publish'),
    [string]$ServiceName = 'EnterpriseChat'
)

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Este script requiere ejecutarse como administrador.'
}

$exePath = Join-Path (Resolve-Path $PublishDir).Path 'EnterpriseChat.Server.exe'
if (-not (Test-Path $exePath)) {
    throw "No se encuentra $exePath. Publica primero con 'dotnet publish -c Release -o $PublishDir'."
}

$existing = sc.exe query $ServiceName 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "Servicio '$ServiceName' ya existe; actualizando binPath."
    sc.exe stop $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    sc.exe config $ServiceName binPath= "`"$exePath`"" start= auto | Out-Null
} else {
    Write-Host "Creando servicio '$ServiceName'."
    sc.exe create $ServiceName binPath= "`"$exePath`"" start= auto DisplayName= 'EnterpriseChat Server' | Out-Null
    sc.exe description $ServiceName 'Servidor de mensajeria EnterpriseChat (open source).' | Out-Null
}

sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
sc.exe start $ServiceName | Out-Null
Write-Host "Servicio '$ServiceName' instalado y arrancado."
