# build-server-windows.ps1
#
# Construye el instalador profesional Windows del servidor EnterpriseChat:
#   1. dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
#   2. ISCC.exe enterprisechat-server.iss
#   3. SHA-256 sidecar para el instalador.
#   4. (Opcional) signtool sign con cert EV si la variable de entorno
#      EC_SIGNCERT_THUMBPRINT esta presente.
#
# Uso:
#   ./build-server-windows.ps1                    # build estandar v0.1.0
#   ./build-server-windows.ps1 -Version 1.0.0     # bump version
#   ./build-server-windows.ps1 -Sign              # firmar con signtool
#
# Salida:
#   .\build\enterprisechat-server-win-x64-<version>.exe
#   .\build\enterprisechat-server-win-x64-<version>.exe.sha256

[CmdletBinding()]
param(
    [string]$Version = '0.1.0',
    [switch]$Sign
)

$ErrorActionPreference = 'Stop'
$Here   = Split-Path -Parent $MyInvocation.MyCommand.Path
$Repo   = Resolve-Path (Join-Path $Here '..\..')
$Server = Join-Path $Repo 'src\EnterpriseChat.Server\EnterpriseChat.Server.csproj'
$Pub    = Join-Path $Repo 'src\EnterpriseChat.Server\bin\publish\win-x64'
$Iss    = Join-Path $Here 'enterprisechat-server.iss'
$Out    = Join-Path $Here 'build'

function Find-IsccExe {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe"
    )
    foreach ($p in $candidates) {
        if (Test-Path $p) { return $p }
    }
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw 'No se encuentra Inno Setup (ISCC.exe). Instala desde https://jrsoftware.org/isdl.php'
}

Write-Host "==> dotnet publish (win-x64, self-contained, single-file)" -ForegroundColor Cyan
if (Test-Path $Pub) { Remove-Item -Recurse -Force $Pub }

dotnet publish $Server `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:DebugType=embedded `
    -p:DebugSymbols=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version `
    -o $Pub

if ($LASTEXITCODE -ne 0) { throw 'dotnet publish fallo' }

# Eliminar appsettings.Development.json del paquete (no debe viajar a prod).
$devSettings = Join-Path $Pub 'appsettings.Development.json'
if (Test-Path $devSettings) { Remove-Item $devSettings }

Write-Host '==> Compilando installer Inno Setup' -ForegroundColor Cyan
$Iscc = Find-IsccExe
Write-Host "    ISCC: $Iscc"

# VersionInfoVersion necesita ser numerico estricto (major.minor.build.revision).
# Si la version trae suffix de pre-release (-alpha.1, -rc.2, etc) le quitamos
# todo a partir del '-' y completamos a 4 partes.
$NumericVersion = ($Version -replace '-.*$','')
$parts = $NumericVersion.Split('.')
while ($parts.Length -lt 4) { $parts += '0' }
$VersionInfo = ($parts[0..3] -join '.')
Write-Host "    Version display:    $Version"
Write-Host "    Version info (PE):  $VersionInfo"

# Invocamos ISCC sobre el .iss ORIGINAL (sin copiarlo a temp) para que los
# paths relativos del script (..\..\LICENSE, ..\..\README.md, ..\..\src\...)
# se resuelvan respecto al repo, no respecto al directorio temporal.
# Los valores variables se inyectan con /D.
New-Item -ItemType Directory -Force -Path $Out | Out-Null
& $Iscc /Q "/O$Out" "/DMyAppVersion=$Version" "/DMyVersionInfoVersion=$VersionInfo" $Iss
if ($LASTEXITCODE -ne 0) {
    throw 'ISCC fallo'
}

$Exe = Get-ChildItem -Path $Out -Filter "enterprisechat-server-win-x64-$Version.exe" | Select-Object -First 1
if (-not $Exe) { throw "No se encontro el exe esperado en $Out" }

# SHA-256 sidecar.
$Sha = (Get-FileHash -Algorithm SHA256 $Exe.FullName).Hash.ToLower()
Set-Content -Path "$($Exe.FullName).sha256" -Value "$Sha  $($Exe.Name)" -Encoding ASCII
Write-Host "==> SHA-256: $Sha" -ForegroundColor Green

# Firma Authenticode opcional.
if ($Sign) {
    $thumb = $env:EC_SIGNCERT_THUMBPRINT
    if (-not $thumb) {
        throw 'EC_SIGNCERT_THUMBPRINT no esta definido. Exporta la huella de tu certificado EV antes de -Sign.'
    }
    Write-Host '==> Firmando con signtool (Authenticode)' -ForegroundColor Cyan
    & signtool.exe sign /sha1 $thumb /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $Exe.FullName
    if ($LASTEXITCODE -ne 0) { throw 'signtool fallo' }
    # Re-hash tras firmar.
    $Sha = (Get-FileHash -Algorithm SHA256 $Exe.FullName).Hash.ToLower()
    Set-Content -Path "$($Exe.FullName).sha256" -Value "$Sha  $($Exe.Name)" -Encoding ASCII
    Write-Host "==> SHA-256 (post-sign): $Sha" -ForegroundColor Green
}

Write-Host ''
Write-Host 'Listo.' -ForegroundColor Green
Write-Host "  Instalador: $($Exe.FullName)"
Write-Host "  SHA-256:    $($Exe.FullName).sha256"
