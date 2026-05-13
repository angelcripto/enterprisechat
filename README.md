# EnterpriseChat

Mensajería corporativa **open source en español**, con arquitectura
cliente-servidor real para entornos profesionales donde el chat tiene que
sobrevivir a un `Win+L`, a un cierre de sesión y a una reconexión sin
romper la conversación del resto de empleados.

> Estado: **Fase 0 — fundación**. Esqueleto de solución, decisiones de
> arquitectura y andamiaje de proyectos. No es funcional todavía.

## Por qué EnterpriseChat

Muchas pymes usan chats internos donde un mismo binario actúa como cliente
y servidor a la vez. Cuando el usuario que tiene "el server" bloquea su
sesión (Win+L), el resto pierde la conexión. EnterpriseChat separa cliente
y servidor de forma real:

- **Servidor**: proceso independiente que corre como **Windows Service**
  o **systemd unit** en una máquina dedicada (o en la del jefe, en el
  rincón). No depende de ninguna sesión interactiva.
- **Cliente**: aplicación de escritorio WPF que se conecta por IP+puerto,
  reconecta sola y mantiene el historial sincronizado.

## Stack

- **.NET 8 LTS** en cliente y servidor.
- **Servidor**: ASP.NET Core minimal host + **SignalR** + **EF Core SQLite**.
  Se ejecuta como Windows Service o systemd unit.
- **Cliente**: **WPF** con `WPF-UI` (Fluent / Mica), MVVM con
  `CommunityToolkit.Mvvm`, MSI generado con Inno Setup.
- **Auth**: contraseñas con **BCrypt** + JWT corto para sesión.
- **TLS** obligatorio en producción.
- **Licenciamiento**: open core. Edición Free con cap de 10 usuarios
  concurrentes; edición Pro vía plugin runtime cerrado.

Las decisiones están documentadas en [docs/adr/](docs/adr/README.md).

## Estructura del repositorio

```
enterprisechat/
├── src/
│   ├── EnterpriseChat.Server/                 # ASP.NET Core + SignalR
│   ├── EnterpriseChat.Client/                 # WPF
│   ├── EnterpriseChat.Protocol/               # DTOs compartidos
│   └── EnterpriseChat.Licensing.Abstractions/ # Interfaz + Free stub
├── tests/
│   └── EnterpriseChat.Tests/                  # xUnit + FluentAssertions
├── docs/
│   └── adr/                                   # Decisiones de arquitectura
├── web/                                       # Sitio público (pendiente)
├── Directory.Build.props
├── Directory.Packages.props                   # Central Package Management
├── global.json                                # SDK .NET 8 pin
├── EnterpriseChat.sln
└── LICENSE                                    # AGPLv3
```

## Compilar y arrancar

Requisitos: **.NET 8 SDK** (`dotnet --version` debe devolver `8.x.x`).

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src/EnterpriseChat.Server
```

Por defecto el servidor escucha en `http://0.0.0.0:5080`. Endpoints:

- `GET /healthz` → estado del proceso.
- `GET /license` → edición y capacidad actual.
- `/hubs/chat` → hub SignalR (vacío todavía).

## Roadmap

| Fase | Entregable |
|------|------------|
| 0 — Fundación | Solución, ADRs, CI, scaffolds (estamos aquí). |
| 1 — MVP servidor | Auth, persistencia, mensajería 1:1, servicio Windows/Linux. |
| 2 — MVP cliente | Login, ventana principal, presencia, notificaciones, instalador. |
| 3 — Producción | Grupos, admin, búsqueda, archivos, licencias firmadas. |
| 4 — v1.0 | Auto-update, i18n, docs completas, web institucional, beta. |

## Licencia

Doble licenciamiento: **AGPLv3** (ver [LICENSE](LICENSE)) o comercial.
Detalles en [COMMERCIAL-LICENSING.md](COMMERCIAL-LICENSING.md).
