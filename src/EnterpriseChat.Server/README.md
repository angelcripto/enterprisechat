# EnterpriseChat.Server

Servidor de EnterpriseChat. ASP.NET Core 8 minimal host con SignalR,
EF Core SQLite y Serilog. El mismo binario corre como aplicación de
consola (desarrollo), como **Windows Service** o como **systemd unit**.

## Endpoints

- `GET /healthz` — devuelve `{ "status": "ok" }`.
- `GET /license` — devuelve la edición y el cap de usuarios actuales.
- `/hubs/chat` — hub SignalR para chat (en construcción).

## Configuración

`appsettings.json` define puertos, conexión SQLite y configuración de
Serilog. `appsettings.Development.json` sobreescribe en local. Variables
de entorno con prefijo estándar de ASP.NET Core funcionan igualmente.

## Licenciamiento

Por defecto se carga `FreeLicenseValidator` (cap de 10 usuarios). Si
existe un fichero `plugins/EnterpriseChat.Licensing.*.dll` que implemente
`ILicenseValidator`, el servidor lo carga al arrancar y lo usa en su
lugar. Esto es lo que permite la edición Pro sin tocar el binario público.

## Despliegue

En desarrollo:

```powershell
dotnet run --project src/EnterpriseChat.Server
```

Como Windows Service (a partir de Fase 1, una vez generados los
instaladores):

```powershell
sc.exe create EnterpriseChat binPath= "C:\Path\To\EnterpriseChat.Server.exe"
sc.exe start EnterpriseChat
```

Como systemd unit (a partir de Fase 1):

```bash
sudo systemctl enable --now enterprisechat.service
```
