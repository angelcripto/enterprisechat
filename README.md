# EnterpriseChat

Mensajería corporativa **open source en español**, con arquitectura
cliente-servidor real para entornos profesionales donde el chat tiene que
sobrevivir a un `Win+L`, a un cierre de sesión y a una reconexión sin
romper la conversación del resto de empleados.

> **Estado: alpha funcional (`0.1.0-alpha.3`).** Servidor, cliente WPF,
> SPA web, applet de bandeja, instalador Windows firmable, licenciamiento
> online y API pública con claves rotables están implementados y operativos.
> Ver el [CHANGELOG](CHANGELOG.md) para el detalle por versión.

## Por qué EnterpriseChat

Muchas pymes usan chats internos donde un mismo binario actúa como cliente
y servidor a la vez. Cuando el usuario que tiene "el server" bloquea su
sesión (Win+L), el resto pierde la conexión. EnterpriseChat separa cliente
y servidor de forma real:

- **Servidor**: proceso independiente que corre como **Windows Service**
  o **systemd unit** en una máquina dedicada (o en la del jefe, en el
  rincón). No depende de ninguna sesión interactiva.
- **Cliente**: aplicación de escritorio WPF que se conecta por IP+puerto,
  reconecta sola y mantiene el historial sincronizado. Alternativamente,
  un SPA Vue 3 servido por el propio servidor para acceso web.

## Qué hay implementado

### Mensajería en tiempo real
- Chat **1:1** con burbujas, timestamps locales y persistencia.
- **Grupos / salas** con visibilidad pública o privada; crear, unirse,
  abandonar, listar miembros.
- **Presencia** online/offline en tiempo real con filtro "solo online".
- **Acuses de recibo**: ticks `✓` (enviado) y `✓✓` (leído) por mensaje.
- **Indicador "escribiendo…"** con throttle de 2 s y TTL de 3 s.
- **Mensajes no leídos** con badges por contacto y por sala.
- **Reacciones** emoji y **mensajes guardados** (bookmarks).
- **Mensajes anclados** (pinned) por sala.
- **Adjuntos** con subida multipart (límite configurable, 10 MB por defecto):
  - Visor inline de imágenes (`png/jpg/jpeg/bmp/gif/webp`) con zoom rueda
    y arrastrar para mover.
  - Resto de tipos (PDF, Word, Excel…) se abren con la aplicación por
    defecto de Windows.
- **Búsqueda global** sobre mensajes accesibles (DMs propios + salas donde
  el usuario es miembro).
- **Reconexión automática exponencial** del cliente (0/2/5/15/30 s).

### Administración
- **Pestaña de Usuarios** en cliente y web: alta, baja (soft + GDPR hard
  delete con anonimización), reset de contraseña, activar/desactivar,
  cambio de rol y departamento.
- **Departamentos** asignables al crear o editar usuarios.
- **Pestaña de Licencia**: estado actual (edición, capacidad, expiración,
  licenciado a), aplicar serial y quitar licencia.
- **Auth providers externos**: OIDC, LDAP, MySQL legacy. Configuración,
  test de conexión, introspect e importación de usuarios desde panel admin.
- **Métricas** (`/metrics`): usuarios activos, mensajes, salas y
  almacenamiento frente a la cuota de licencia.
- **Audit log** completo en BD (logins OK/KO, sesiones, acciones admin).
- **Reset de contraseña admin** vía CLI:
  `dotnet run --project src\EnterpriseChat.Server -- --reset-admin-password "nuevaClave"`.

### Licenciamiento online (autoridad: backend PHP)
- El admin pega un serial opaco **`ECP-XXXX-XXXX-XXXX-XXXX`** (Base32
  Crockford, tolerante a typos `I→1`, `O→0`, `U→V`) en la pestaña Licencia.
- El servidor llama a `POST /activate` del backend; éste ata el serial al
  hardware (hostname + mac hash + ip) y devuelve un **JWT RS256** corto.
- Persistencia en tabla `Licenses`, heartbeat periódico, restauración al
  arrancar (`LicenseStartupRestorer`) y degradación graciosa a
  **`FreeLicenseValidator`** (cap 10 usuarios concurrentes) si el backend
  no es alcanzable.
- Revocación remota detectada en el siguiente heartbeat (CRL pública
  `/revoked.json` en el backend).
- Cuenta **usuarios distintos**, no conexiones: WPF + web + móvil del
  mismo user = 1 slot.

### API pública con claves rotables (PAT)
- Tokens **`ec_pat_<base64url>`** entregados una sola vez al crear o rotar
  (sólo se persiste `SHA-256(token)` en BD).
- `ApiKeyAuthenticationHandler` + `PolicyScheme` `JwtOrApiKey` permite
  usar las mismas policies con JWT humano o PAT de servicio.
- Endpoints admin `POST/GET/DELETE /admin/api-keys[/{id}[/rotate|revoke]]`
  bajo policy `AdminOnly`. UI en `/manage/api-keys`.
- **Rate limit** de 60 req/min por PAT (bucket fijo particionado por
  `jti`), `429` + `Retry-After: 60` al rechazar. El JWT humano no se
  limita.
- **Documentación pública sin auth**: Swagger UI en `/docs/api`, OpenAPI
  3.0 en `/docs/openapi/v1.json`, redirect `/docs → /docs/api/`, y
  markdowns narrativos en `/docs/{getting-started,authentication,signalr-hub,errors}.md`.
- Ver [ADR-0005](docs/adr/0005-api-publica-con-claves-rotables.md) para
  la racional completa.

### Operación
- **Windows Service** y **systemd unit** desde el mismo binario.
- **Instalador Windows** profesional (Inno Setup) que publica un single-file
  self-contained, genera al vuelo `Jwt:SigningKey` (48 B base64) y password
  admin (16 chars), escribe `appsettings.Production.json`, registra el
  servicio con `sc.exe` y muestra credenciales al final. Firma EV opcional.
- **Applet de bandeja** (`EnterpriseChat.TrayMonitor`): arrancar / parar /
  reiniciar el servicio, ver health HTTP, cambiar la contraseña admin sin
  abrir el cliente, banner de elevación si no es admin.
- **Bootstrap interactivo** con Spectre.Console al primer arranque en
  consola: pregunta Test/Prod, autogenera secrets, pide password admin.
- **Pre-bind check** del puerto antes de Kestrel con panel rojo y
  sugerencias `netstat`/`ss` si está ocupado.
- **Master key** persistida en `data/master.key` para cifrar credenciales
  de auth providers en BD (configurable vía `EnterpriseChat:Crypto:MasterKey`).

## Stack

- **.NET 8 LTS** (SDK pin: `8.0.413`).
- **Servidor**: ASP.NET Core minimal host + **SignalR** + **EF Core SQLite**
  + **Serilog** + **Spectre.Console** + **Swashbuckle**.
- **Cliente de escritorio**: **WPF** (`net8.0-windows`) con
  [`WPF-UI`](https://github.com/lepoco/wpfui) (Fluent / Mica), MVVM con
  `CommunityToolkit.Mvvm`. Instalador Inno Setup.
- **SPA web**: **Vue 3** + **Pinia** + **Vue Router** +
  **`@microsoft/signalr`** + **TailwindCSS 4** + **Vite 6** +
  **TypeScript**. Se compila a `wwwroot/` y se sirve desde el mismo puerto
  que la API.
- **Auth local**: contraseñas con **BCrypt** (cost 12) + **JWT HS256**
  corto. Providers externos: OIDC, LDAP, MySQL legacy.
- **Licenciamiento**: open core. Free con cap 10 usuarios concurrentes;
  Pro vía backend PHP (`web-enterprisechat`, repo hermano privado) que
  firma JWT RS256.

Decisiones documentadas en [docs/adr/](docs/adr/README.md):

1. [Open core: monorepo AGPL + plugin Pro](docs/adr/0001-open-core-monorepo-licensing-plugin.md).
2. [Cliente WPF (no WinUI 3)](docs/adr/0002-cliente-wpf-en-lugar-de-winui3.md).
3. [Transporte SignalR (no WebSockets puros)](docs/adr/0003-transporte-signalr-en-lugar-de-websockets-puros.md).
4. [BCrypt (no Argon2id) para passwords](docs/adr/0004-password-hashing-bcrypt-en-lugar-de-argon2.md).
5. [API pública con claves rotables](docs/adr/0005-api-publica-con-claves-rotables.md).

## Estructura del repositorio

```
enterprisechat/
├── src/
│   ├── EnterpriseChat.Server/                 # ASP.NET Core + SignalR + SPA Vue
│   │   ├── web/                               # Vue 3 + Vite + TS (compila a wwwroot/)
│   │   ├── Bootstrap/                         # InteractiveLauncher, MasterKey, AdminSeeder
│   │   ├── Data/Migrations/                   # 9 migraciones EF Core
│   │   ├── Hubs/                              # ChatHub (SignalR)
│   │   ├── Endpoints/                         # REST endpoints agrupados
│   │   ├── Licensing/                         # Cliente del backend remoto + heartbeat
│   │   └── ApiKeys/                           # PAT auth handler, servicio, rate limit
│   ├── EnterpriseChat.Client/                 # Cliente WPF (Fluent/Mica)
│   ├── EnterpriseChat.TrayMonitor/            # Applet bandeja para gestionar el servicio
│   ├── EnterpriseChat.Protocol/               # DTOs compartidos cliente↔servidor
│   └── EnterpriseChat.Licensing.Abstractions/ # ILicenseValidator/Administrator + Free stub
├── tests/EnterpriseChat.Tests/                # xUnit + FluentAssertions + WebApplicationFactory
├── installer/windows/                         # Inno Setup + build-server-windows.ps1
├── docs/adr/                                  # 5 ADRs
├── Directory.Build.props
├── Directory.Packages.props                   # Central Package Management
├── global.json                                # SDK .NET 8 pin
├── EnterpriseChat.sln
└── LICENSE                                    # AGPLv3
```

## Instalación

### Windows (usuarios finales) — recomendado

Descarga el instalador (Inno Setup, single-file self-contained) desde
[releases](https://github.com/anthropics/enterprisechat/releases) y
ejecútalo. El instalador:

1. Copia el binario a `C:\Program Files\EnterpriseChat`.
2. Genera al vuelo `Jwt:SigningKey` y contraseña admin (16 chars).
3. Escribe `appsettings.Production.json` y `.first-admin-password` con
   las credenciales en claro (sólo presentes al primer arranque).
4. Registra el servicio Windows `EnterpriseChat` (arranque automático).
5. Muestra al final URL + usuario + password.

Tras instalar, el **applet de bandeja** (`EnterpriseChat.TrayMonitor`)
permite arrancar/parar el servicio y cambiar la contraseña admin.

### Linux (systemd)

Hay un unit en `installer/linux/enterprisechat.service` y un script
`install-linux.sh`. Publica el servidor con
`dotnet publish src/EnterpriseChat.Server -c Release -r linux-x64
--self-contained`, copia el binario, registra el unit y arranca.

### Desarrollo

Requisitos: **.NET 8 SDK** (`dotnet --version` ≥ `8.0.413`) y, si tocas el
SPA, **Node.js 20+**.

```powershell
dotnet restore EnterpriseChat.sln
dotnet build EnterpriseChat.sln
dotnet test EnterpriseChat.sln
.\start-server.cmd     # arranca el servidor con launcher interactivo
.\start-client.cmd     # arranca el cliente WPF
```

SPA Vue en hot-reload (`http://localhost:5173`, proxy a `:5080`):

```powershell
cd src\EnterpriseChat.Server\web
npm install
npm run dev
```

Para builds más rápidos del servidor durante iteración:

```powershell
dotnet build src\EnterpriseChat.Server -p:BuildSpa=false           # salta el SPA
dotnet build src\EnterpriseChat.Server -p:RunSpaTypeCheck=true     # type-check completo del SPA
```

Construir el instalador Windows:

```powershell
cd installer\windows
.\build-server-windows.ps1                   # alpha sin firma
.\build-server-windows.ps1 -Version 1.0.0
.\build-server-windows.ps1 -Sign             # requiere EC_SIGNCERT_THUMBPRINT
.\build-server-windows.ps1 -SkipPublish      # iterar el .iss sin republicar
```

## Endpoints principales

El servidor escucha por defecto en `http://0.0.0.0:5080` (configurable en
`Kestrel:Endpoints:Http:Url`). Categorías:

- **Salud y docs públicos**: `GET /healthz`, `GET /license`,
  `GET /docs/api` (Swagger UI), `GET /docs/openapi/v1.json`.
- **Auth**: `POST /auth/login`.
- **Mensajería**: hub SignalR en `/hubs/chat` con métodos
  `SendDirectMessage`, `SendRoomMessage`, `GetRoomHistory`,
  `GetDirectHistory`, `CreateRoom`, `JoinRoom`, `LeaveRoom`, `MarkAsRead`,
  `Typing`, y callbacks `OnMessageReceived`, `OnPresenceChanged`,
  `OnRoomMembershipChanged`, `OnMessageRead`, `OnTyping`,
  `OnPinnedChanged`, `OnReactionChanged`, `OnLicenseDenied`.
- **Usuarios y avatares**: `GET /users`, `GET/POST/DELETE
  /users/me/avatar`, `GET /users/{id}/avatar`.
- **Salas**: `GET /rooms`, `GET /rooms/{id}/files`, `GET/POST/DELETE
  /rooms/{id}/pinned/{messageId}`.
- **Mensajes (engagement)**: `GET/POST /messages/{id}/reactions`,
  `POST /messages/{id}/save`, `GET /me/inbox`, `GET /me/mentions`,
  `GET /me/saved`, `GET /metrics`.
- **Archivos**: `POST /files` (upload multipart), `GET /files/{id}`.
- **Búsqueda**: `GET /search?q=…&limit=50`.
- **Admin** (`RequireRole("Admin")`): `/admin/users[/...]`,
  `/admin/departments`, `/admin/license`, `/admin/api-keys[/...]`,
  `/admin/auth-providers[/...]`.

Detalle completo en la **Swagger UI pública** (`/docs/api`) y en los
[markdowns narrativos](src/EnterpriseChat.Server/Docs).

## Tests y CI

- **20 archivos de tests** (xUnit + FluentAssertions +
  `Microsoft.AspNetCore.Mvc.Testing` con `WebApplicationFactory`
  in-process). Cubren auth, crypto (Argon2id verifier, AppCrypto),
  hub round-trips, rooms, search, admin endpoints, licensing
  (`FreeLicenseValidator`, `ConcurrentSessionCounter`), API keys
  (persistencia, auth, servicio, rate limit), bootstrap del host y
  Swagger.
- **CI** en GitHub Actions
  ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)):
  - Job **Windows** (`windows-latest`): `dotnet restore` →
    `dotnet format --verify-no-changes` (bloqueante) → `build` →
    `test` con artefactos TRX.
  - Job **Linux** (`ubuntu-latest`): build sólo del servidor (WPF no se
    construye en Linux).

## Cómo encaja con el backend de licencias

Repo hermano **`web-enterprisechat`** (privado, Slim 4 + MySQL) es la
autoridad de licencias. La pareja:

1. Admin pega un serial opaco en la pestaña Licencia.
2. El servidor llama a `POST /activate` del backend, que devuelve un JWT
   RS256 corto firmado con `keys/license_private.pem` (que **nunca** entra
   en este repo).
3. El JWT se valida con la pública embebida en el plugin Pro y se
   persiste en la tabla `Licenses`.
4. Heartbeat periódico renueva TTL y detecta revocaciones (CRL en
   `/revoked.json`).

La clave privada de firma vive **sólo** en el repo privado. El servidor
.NET embebe únicamente la pública del plugin Pro.

## Roadmap

| Fase | Estado | Entregable |
|------|:------:|------------|
| 0 — Fundación | ✅ | Solución, ADRs, CI, Central Package Management, scaffolds. |
| 1 — MVP servidor | ✅ | Auth, persistencia, DMs, hub SignalR, Windows Service / systemd. |
| 2 — MVP cliente | ✅ | Cliente WPF completo, instalador, applet de bandeja. |
| 3 — Producción | ✅ | Grupos, admin, búsqueda, archivos, licencias online, providers externos, API pública. |
| 4 — v1.0 | 🚧 | Auto-update, i18n, docs completas, web institucional, beta pública. |

## Contribuir

Ver [CONTRIBUTING.md](CONTRIBUTING.md). El proyecto entero (CHANGELOG,
ADRs, comentarios, mensajes de log, UI) está en **español**; el código y
los identificadores siguen en inglés.

## Licencia

Doble licenciamiento: **AGPLv3** (ver [LICENSE](LICENSE)) o comercial.
Detalles en [COMMERCIAL-LICENSING.md](COMMERCIAL-LICENSING.md).
