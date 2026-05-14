# Instalador Windows (Inno Setup)

Build profesional del servidor EnterpriseChat para Windows como un único
`.exe` autoinstalable con wizard (Welcome → License AGPLv3 → Carpeta →
Instalando → Final con contraseña admin).

## Requisitos en la máquina de build

- **.NET 8 SDK** (`dotnet --version` debe imprimir `8.x.x`).
- **Inno Setup 6** — instalador en
  [jrsoftware.org/isdl.php](https://jrsoftware.org/isdl.php). El script
  `build-server-windows.ps1` localiza `ISCC.exe` en las rutas estándar
  (`Program Files (x86)\Inno Setup 6`); también acepta `ISCC.exe` en
  `PATH`.
- PowerShell 5.1+ o 7.x.

## Construir

```powershell
cd installer\windows
./build-server-windows.ps1                  # build 0.1.0 sin firma
./build-server-windows.ps1 -Version 1.0.0   # cambiar versión
./build-server-windows.ps1 -Sign            # firmar con signtool/EV cert
./build-server-windows.ps1 -SkipPublish     # solo recompilar el .iss
```

`-SkipPublish` reutiliza el output anterior de
`src\EnterpriseChat.Server\bin\publish\win-x64`. Útil para iterar sobre
el script Inno Setup sin esperar a `dotnet publish` cada vez (corta el
ciclo de ~30s a ~3s).

Salida:

```
installer\windows\build\enterprisechat-server-win-x64-<version>.exe
installer\windows\build\enterprisechat-server-win-x64-<version>.exe.sha256
```

## Firma Authenticode

Para firmar exporta la huella de tu certificado EV y pasa `-Sign`:

```powershell
$env:EC_SIGNCERT_THUMBPRINT = '1234ABCD...EF'
./build-server-windows.ps1 -Sign
```

El script invoca `signtool.exe sign /sha1 <thumb> /fd SHA256 /tr
http://timestamp.digicert.com /td SHA256 <exe>`. SmartScreen sin firma EV
mostrará la pantalla amarilla las primeras descargas hasta acumular
reputación; con EV es instantáneo.

## Modo silent

El instalador soporta los flags estándar de Inno Setup. El script
`install.ps1` que publica la web los usa para instalar sin interacción:

```powershell
installer.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

Otros flags útiles:

| Flag | Efecto |
|---|---|
| `/SILENT` | Muestra barra de progreso, sin diálogos |
| `/VERYSILENT` | Sin ninguna UI |
| `/SUPPRESSMSGBOXES` | Auto-OK en cualquier MessageBox |
| `/DIR="C:\ruta"` | Override del directorio de instalación |
| `/NORESTART` | No reiniciar incluso si el setup lo pide |
| `/LOG="ruta.log"` | Volcar log detallado a archivo |

## Qué hace por dentro

1. **Files** — Copia el publish self-contained completo (binario,
   `wwwroot/`, `appsettings.json`, `LICENSE`) a `{app}` (por defecto
   `C:\Program Files\EnterpriseChat`).
2. **Dirs** — Crea `{app}\data`, `{app}\logs`, `{app}\certs` con
   permisos de escritura para el grupo Users.
3. **Code (Pascal)** — Genera al vuelo:
   - JWT signing key — 48 bytes base64.
   - Admin password — 16 chars alfanuméricos.
   - Los escribe en `{app}\appsettings.Production.json` si no existe.
   - Guarda la contraseña en `{app}\.first-admin-password` para que el
     admin la consulte si pierde la pantalla final.
4. **Run** — Registra el Windows Service `EnterpriseChat` con
   `sc.exe create ... start= auto`, lo describe, y lo arranca si el admin
   dejó marcada la casilla "Arrancar servicio al finalizar".
5. **Final** — Pantalla con URL admin + usuario + contraseña en claro.

## Uninstaller

El uninstaller detiene y elimina el servicio, borra archivos de programa
y limpia logs. **Los datos** (`{app}\data\chat.db`, adjuntos, certs,
`appsettings.Production.json`) se conservan por defecto; el wizard
pregunta si quieres purgarlos.

## Estructura

```
installer/windows/
├── enterprisechat-server.iss     Script Inno Setup
├── build-server-windows.ps1      Orquestador (publish + ISCC + sign)
├── README.md                     Este archivo
└── build/                        Salida (gitignored)
```
