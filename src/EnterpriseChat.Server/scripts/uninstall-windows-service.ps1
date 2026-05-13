<#
.SYNOPSIS
    Stops and unregisters EnterpriseChat.Server from the Windows Service catalog.

.PARAMETER ServiceName
    Default 'EnterpriseChat'.
#>

[CmdletBinding()]
param(
    [string]$ServiceName = 'EnterpriseChat'
)

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Este script requiere ejecutarse como administrador.'
}

sc.exe query $ServiceName 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "El servicio '$ServiceName' no esta instalado."
    exit 0
}

sc.exe stop $ServiceName | Out-Null
Start-Sleep -Seconds 2
sc.exe delete $ServiceName | Out-Null
Write-Host "Servicio '$ServiceName' desinstalado."
