# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Qué es esto

Núcleo open source AGPLv3 de **EnterpriseChat**: chat corporativo con cliente y servidor **realmente** separados (el servidor sobrevive a `Win+L` y a cierres de sesión del puesto donde corra). Mismo binario sirve como consola en dev, **Windows Service** vía `sc.exe` o **systemd unit** en Linux.

**Estado:** Fase 0/1 — fundación y MVP servidor en marcha. El [CHANGELOG](CHANGELOG.md) es la fuente de verdad de qué hay implementado.

## Estructura de la solución

[EnterpriseChat.sln](EnterpriseChat.sln) agrupa 6 proyectos en 2 carpetas (`src/`, `tests/`):

- **[EnterpriseChat.Server](src/EnterpriseChat.Server)** — ASP.NET Core + SignalR + EF Core SQLite. Hosting Windows Service / systemd. Incluye el SPA Vue 3 en [src/EnterpriseChat.Server/web](src/EnterpriseChat.Server/web) que `dotnet build` compila a `wwwroot/` (ver target `BuildVueSpa` en el csproj).
- **[EnterpriseChat.Client](src/EnterpriseChat.Client)** — WPF (`net8.0-windows`) con `WPF-UI` (Fluent/Mica) + MVVM `CommunityToolkit.Mvvm`. Es el cliente "gordo" de escritorio. Coexiste con el SPA Vue (que sirve el mismo servidor para acceso web).
- **[EnterpriseChat.TrayMonitor](src/EnterpriseChat.TrayMonitor)** — WPF aparte: applet de bandeja que arranca/para el Windows Service y lanza el cambio de contraseña admin sin abrir el cliente.
- **[EnterpriseChat.Protocol](src/EnterpriseChat.Protocol)** — DTOs compartidos cliente↔servidor (Login, ChatMessage, IChatClient, Search, Rooms, Files, Admin, Licensing).
- **[EnterpriseChat.Licensing.Abstractions](src/EnterpriseChat.Licensing.Abstractions)** — `ILicenseValidator` + `ILicenseAdministrator` + `FreeLicenseValidator/Administrator` stub. El plugin Pro (repo cerrado aparte) implementa estas interfaces.
- **[EnterpriseChat.Tests](tests/EnterpriseChat.Tests)** — xUnit + FluentAssertions + `Microsoft.AspNetCore.Mvc.Testing` (WebApplicationFactory in-process).

## Comandos

Requiere **.NET 8 SDK** (pin en [global.json](global.json) — `8.0.413`, `rollForward: latestFeature`).

```powershell
dotnet restore EnterpriseChat.sln
dotnet build EnterpriseChat.sln
dotnet test EnterpriseChat.sln                                          # toda la suite
dotnet test --filter "FullyQualifiedName~ChatHubTests"                  # un grupo
dotnet test --filter "FullyQualifiedName=...ChatHubTests.SendsDmRoundTrip"  # un test concreto
dotnet format EnterpriseChat.sln --verify-no-changes                    # mismo check que CI

# Arrancar el servidor (en consola; activa el launcher interactivo)
.\start-server.cmd
# o equivalente:
dotnet run --project src\EnterpriseChat.Server

# Build acelerado del server saltando el SPA (útil cuando no tocas Vue
# o no tienes Node instalado)
dotnet build src\EnterpriseChat.Server -p:BuildSpa=false

# Type-check completo del SPA (apagado por defecto para acelerar el build iterativo)
dotnet build src\EnterpriseChat.Server -p:RunSpaTypeCheck=true

# Resetear la contraseña del admin sin tocar la BD a mano
dotnet run --project src\EnterpriseChat.Server -- --reset-admin-password "nuevaClave"

# Migraciones EF Core (dotnet-ef pinneado en .config/dotnet-tools.json)
dotnet tool restore
dotnet ef migrations add <Nombre> --project src\EnterpriseChat.Server --output-dir Data/Migrations
```

**SPA Vue en hot-reload** (puerto distinto del server, proxy a la API):

```powershell
cd src\EnterpriseChat.Server\web
npm install
npm run dev     # http://localhost:5173, proxy a http://localhost:5080
```

Vite proxea `/auth`, `/users`, `/rooms`, `/search`, `/files`, `/admin`, `/license`, `/healthz` y `/hubs` (WebSocket) al servidor C# en `:5080`. Ver [vite.config.ts](src/EnterpriseChat.Server/web/vite.config.ts).

**Instalador Windows** (Inno Setup + dotnet publish single-file self-contained):

```powershell
cd installer\windows
./build-server-windows.ps1                  # v0.1.0 sin firma
./build-server-windows.ps1 -Version 1.0.0
./build-server-windows.ps1 -Sign            # requiere EC_SIGNCERT_THUMBPRINT
./build-server-windows.ps1 -SkipPublish     # iterar el .iss sin republicar (~30s→3s)
```

Ver [installer/windows/README.md](installer/windows/README.md).

## Convenciones de código

[.editorconfig](.editorconfig) es estricto y forma parte del build:

- `TreatWarningsAsErrors=true` en [Directory.Build.props](Directory.Build.props) — un warning de Roslyn rompe la compilación. CI corre `dotnet format --verify-no-changes` después de `restore`, así que el formato también es bloqueante.
- `Nullable` y `ImplicitUsings` activados en todo.
- `csharp_style_namespace_declarations = file_scoped:warning`, campos privados con `_camelCase`.
- `Microsoft.NET.Sdk.Web` para Server, `Microsoft.NET.Sdk` (WPF) para Client/TrayMonitor.
- **Central Package Management** vía [Directory.Packages.props](Directory.Packages.props): añadir un paquete = `PackageVersion` aquí + `PackageReference` (sin versión) en el csproj que lo usa.
- Las migraciones EF tienen el bloque `[**/Migrations/*.cs]` con varios diagnostics desactivados; **no luches con el generador en código generado**, edítalo sólo si EF lo regenera tras un `migrations add`.

## Detalles de arquitectura que no se ven leyendo un único fichero

### Mismo binario, tres hostings — y el `WorkingDirectory` del SCM

[Program.cs](src/EnterpriseChat.Server/Program.cs) llama a `UseWindowsService()` y `UseSystemd()` de forma incondicional: la presencia del entorno decide cuál se activa. **Importante**: cuando arranca como Windows Service, `sc.exe` deja `CurrentDirectory = C:\Windows\System32`. Eso rompería todas las rutas relativas (`data/chat.db`, `wwwroot/`, logs). Por eso `Program.cs` fuerza `ContentRootPath = AppContext.BaseDirectory` y hace `Directory.SetCurrentDirectory(...)` antes de construir el host. No quites ese bloque "porque parece redundante con systemd": Linux ya tiene `WorkingDirectory=` en la unit; Windows no.

### El launcher interactivo se autosalta cuando no hay TTY

`InteractiveLauncher.RunIfInteractive` ([Bootstrap/InteractiveLauncher.cs](src/EnterpriseChat.Server/Bootstrap/InteractiveLauncher.cs)) sólo lanza el wizard Spectre (Test/Prod, autogenera `Jwt:SigningKey` 48 B base64, pide contraseña admin en Prod) si: hay TTY, no se pasa `--service`/`--no-interactive`, no hay `ASPNETCORE_ENVIRONMENT` ya seteado y stdin/stdout no están redirigidos. Por eso [start-server.cmd](start-server.cmd) **no** fuerza `ASPNETCORE_ENVIRONMENT`: deja que el launcher pregunte. Saltarse el prompt = `set ASPNETCORE_ENVIRONMENT=Development` o `--no-interactive`.

### Master key persistida en `data/master.key` SIEMPRE

[MasterKeyInitializer](src/EnterpriseChat.Server/Bootstrap/MasterKeyInitializer.cs) genera/lee 32 bytes en `{ContentRoot}/data/master.key` en Development, Testing y Production. **No** vive sólo en memoria: si lo hiciera, cada reinicio invalidaría los blobs cifrados (credenciales de providers OIDC/LDAP/MySQL guardadas en BD) y la UI lanzaría "computed authentication tag did not match" tras el primer restart en dev. Borrar el fichero = perder esos secretos. Si el operador prefiere inyectar la key desde un secret manager, definir `EnterpriseChat:Crypto:MasterKey` en config tiene preferencia sobre el fichero. La key se inicializa **antes** que los providers, no es opcional.

### Pre-bind check de puerto antes de Kestrel

`PortAvailabilityCheck.TryEnsureAvailable` hace un `TcpListener` previo en `IPAddress.Any:<puerto>` antes de que Kestrel arranque. Si choca, renderiza un panel rojo Spectre con sugerencias `netstat`/`ss` y espera tecla antes de salir (sólo si es interactivo). Además, el `catch` de `Program.cs` recorre toda la cadena `InnerException` buscando `SocketError.AddressAlreadyInUse` por si Kestrel petara post-check (race condition). **No** colapses los dos caminos — el pre-check captura el 95% de los casos con un mensaje limpio; el catch es la red de seguridad.

### Connection string resuelta perezosamente

[PersistenceExtensions.AddChatPersistence](src/EnterpriseChat.Server/Data/PersistenceExtensions.cs) lee `ConnectionStrings:Sqlite` **dentro** del callback de configuración del `DbContextFactory`, no al registrar. Los tests usan `WebApplicationFactory<Program>` y sobrescriben la connection string vía `ConfigureAppConfiguration`, que se ejecuta **después** de `Program.cs`. Si se leyera la cs en `AddChatPersistence`, los tests escribirían siempre en el `data/chat.db` relativo al directorio del runner — bug ya cazado, no reintroducir.

### Búsqueda con `LIKE`, no FTS5

La migración `AddMessagesFts` (20260513113003) es **deliberadamente no-op**: FTS5 con triggers no funciona bien con EF Core migrations en este setup. `/search` ordena por `Messages.Id` (SQLite no permite `ORDER BY DateTimeOffset`). Si se vuelve a FTS5, hacerlo con SQL puro fuera del modelo EF.

### Licensing online: PHP backend es la autoridad

[LicensingExtensions](src/EnterpriseChat.Server/Licensing/LicensingExtensions.cs) **ya no carga plugins por reflexión** desde `plugins/`. El pipeline actual es:

- `RemoteLicenseState` singleton compartido (estado actual + última activación).
- `RemoteLicenseValidator` (lee de `RemoteLicenseState`) → registrado como `ILicenseValidator`.
- `RemoteLicenseAdministrator` (escribe → `RemoteLicenseState`) → registrado como `ILicenseAdministrator`.
- `LicenseActivationClient` con `IHttpClientFactory` (timeout 15s, UA fijo) habla con `EnterpriseChat:Licensing:ActivationUrl` (default `https://enterprisechat.es/activate`).
- `LicenseHeartbeatService` (`IHostedService`) re-activa periódicamente.
- `LicenseStartupRestorer` re-aplica al arrancar la fila `active` de la tabla `Licenses`.

Si el backend remoto es inalcanzable, **se cae a `FreeLicenseValidator`** (cap 10 usuarios). El servidor sigue funcionando.

### `ConcurrentSessionCounter` cuenta usuarios distintos, no conexiones

[ConcurrentSessionCounter](src/EnterpriseChat.Server/Licensing/ConcurrentSessionCounter.cs) cuenta IDs únicos de usuario conectados, no `connectionId`s de SignalR. Un usuario con cliente WPF + pestaña web + móvil = 1 slot consumido. No cambies esto sin actualizar la columna `max_users` del licensing.

### Programa expuesto para tests

[Program.cs](src/EnterpriseChat.Server/Program.cs) acaba con `public partial class Program;` porque los top-level statements emiten `internal class Program` y `WebApplicationFactory<Program>` necesita acceso desde el ensamblado de tests. No quitar la línea.

### El SPA Vue y el cliente WPF son interfaces alternativas

El servidor sirve **ambos**: el cliente WPF habla con la API y el hub directamente; el SPA Vue se compila a `wwwroot/` y se sirve desde el mismo puerto. `app.MapFallbackToFile("index.html")` deja que Vue Router maneje rutas SPA tipo `/channels/42`. En `Development`, CORS para `:5173` está habilitado (política `WebSpaDev`); en Release no, porque SPA y API comparten origen.

## Decisiones documentadas

Las ADRs viven en [docs/adr/](docs/adr/README.md). Decisiones inmutables: si una cambia, se añade nueva ADR marcando la anterior como Superseded. Hasta ahora:

1. Open core (monorepo público AGPL + plugin Pro cerrado).
2. Cliente WPF (no WinUI 3).
3. Transporte SignalR (no WebSockets puros).
4. BCrypt (no Argon2id) para passwords.

## CI

[.github/workflows/ci.yml](.github/workflows/ci.yml) sobre `windows-latest`:

1. `dotnet restore EnterpriseChat.sln`.
2. **`dotnet format --verify-no-changes`** (rompe el build si hay diff de formato).
3. `dotnet build --configuration Release`.
4. `dotnet test` con logger TRX → sube artefacto.

Job paralelo `build-linux-server` compila sólo `EnterpriseChat.Server.csproj` en Ubuntu (WPF no se construye en Linux; ese path se cubre con el job Windows).

## Cómo encaja con el backend de licencias

Repo hermano [../web-enterprisechat/](../web-enterprisechat) (Slim 4 + MySQL, ver su `CLAUDE.md`) es la autoridad de licencias. La pareja:

- El admin pega un serial opaco `ECP-XXXX-XXXX-XXXX-XXXX` en la pestaña Licencia del cliente WPF (o `POST /admin/license` directamente).
- El servidor llama a `POST /activate` del backend, que devuelve un JWT RS256 corto.
- El JWT se valida con la pública embebida en el plugin Pro y se persiste en la tabla `Licenses` de este servidor.
- Heartbeat periódico re-activa para renovar TTL y detectar revocaciones (CRL en `/revoked.json`).

La clave privada de firma **nunca** entra en este repo — vive sólo en `web-enterprisechat/keys/license_private.pem`.
