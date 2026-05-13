# Changelog

El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/)
y este proyecto adhiere a [Semantic Versioning](https://semver.org/lang/es/).

## [Unreleased]

## [0.1.0-alpha.2] — 2026-05-13

### Added — Fase 2 (MVP cliente WPF)

- App.xaml.cs con `Host.CreateDefaultBuilder` + Serilog → `%LOCALAPPDATA%\EnterpriseChat\logs`.
- `SettingsStore` persiste `%APPDATA%\EnterpriseChat\settings.json` con `ServerUrl` y último usuario.
- `LoginViewModel` + `LoginWindow` FluentWindow: health-check `/healthz` previo, oculta form si el servidor no responde y muestra botón **Reintentar**.
- `MainViewModel` + `MainWindow` con barra superior (badge de estado conectado/reconectando/desconectado con dot circular, URL servidor, usuario actual, edición, botones **Buscar** / **Administración** / **Configurar**) y panel chat 1:1.
- `ConnectionSettingsWindow` modal para editar `ServerUrl` desde login o main.
- `ChatClient` wrapper de `HubConnection` con reconexión exponencial.
- Sidebar contactos con presencia (verde/gris), filtro **Solo online**, badges rojos de mensajes no leídos que se borran al seleccionar contacto.
- Preloader overlay durante connect+contacts.
- Notificación tray (`Wpf.Ui.Tray`) con menú **Mostrar / Cerrar sesión / Salir**, minimiza al cerrar (X), tooltip y título dinámicos con username para distinguir instancias.
- Enter envía mensaje en chat y en login.

### Added — Fase 3 (grupos, admin, receipts, búsqueda, archivos)

- Servidor: entidades `Room` y `RoomMember` + migración `AddRooms`.
- Hub: `CreateRoom`, `JoinRoom`, `LeaveRoom`, `SendRoomMessage`, `GetRoomHistory`, broadcast `OnRoomMembershipChanged`.
- Endpoint `GET /rooms` listando salas visibles (públicas + propias) con `IsMember` y `MemberCount`.
- Cliente: pestaña **Grupos** en sidebar, `CreateRoomWindow` modal, `RoomViewModel` + `RoomConversationViewModel`, badges de no leídos por sala y entrar/salir desde la UI.
- Acuses de recibo: hub `MarkAsRead` actualiza `Messages.ReadAt`, broadcasting `OnMessageRead`. Renderizado en burbujas con ticks `✓` (enviado) y `✓✓` (leído).
- Indicador "escribiendo…" con throttle 2 s y TTL 3 s; visible bajo el header de la conversación.
- Servidor: endpoints `/admin/users` (GET/POST/PUT/DELETE), `/admin/users/{id}/reset-password`, `/admin/departments`, todos con `RequireRole("Admin")`.
- Cliente: `AdminWindow` con pestañas **Usuarios** (DataGrid + crear + reset + activar/desactivar) y **Departamentos** (lista + crear). Botón visible solo si rol Admin.
- Búsqueda: endpoint `GET /search?q=&limit=` con filtro de visibilidad (DMs propios + salas miembro) usando `LIKE` (FTS5 abandonado en este sprint por incompatibilidad con triggers en EF Core migrations en este entorno; ver migración `AddMessagesFts` no-op).
- Cliente: `SearchWindow` modal con resultados (autor, fecha, sala/DM).
- Archivos: entidad `Attachment` + migración `AddAttachments`, endpoints `POST /files` (multipart, límite configurable `EnterpriseChat:Server:MaxAttachmentSizeBytes`, default 10 MB) y `GET /files/{id}` con autorización por visibilidad del mensaje asociado.
- Hub: `SendDirectMessageWithAttachment` y `SendRoomMessageWithAttachment`.
- Cliente: botón 📎 en chat input que abre `OpenFileDialog`, sube y envía mensaje con adjunto; chip clicable bajo cada mensaje para descargar.
- ChatMessage extendido con `AttachmentId`, `AttachmentFileName`, `AttachmentSizeBytes`.
- Tests: 36/36 verde (auth, admin, rooms, search, hub bootstrap, chat hub DM, licensing, protocol).

### Fixed

- `ChatServerFactory` ahora aplica overrides vía `ConfigureWebHost` y `PersistenceExtensions.AddChatPersistence` resuelve la connection string desde DI lazy. Antes los tests escribían al `data/chat.db` relativo al directorio del runner por orden de configuración.
- Endpoint `/search` ordena por `Messages.Id` (SQLite no permite `ORDER BY DateTimeOffset`).
- `SignalR` user-id provider lee `sub` además de `ClaimTypes.NameIdentifier`.

## [0.1.0-alpha.1] — 2026-05-13

### Added — Fase 1 (MVP servidor)

- Capa de persistencia EF Core con SQLite y migración inicial
  (`Users`, `Departments`, `Sessions`, `Messages`, `AuditLog`). WAL
  activado en arranque.
- Hashing de contraseñas con BCrypt (cost factor 12) detrás de
  `IPasswordHasher` para permitir migración futura.
- Emisión de JWT HS256 con claims `sub`, `jti`, `name`, `role` y vida
  configurable. Bearer scheme acepta el token vía cabecera o query string
  `access_token` (necesario para SignalR sobre WebSocket).
- `IUserIdProvider` propio que lee el claim `sub` para que
  `Clients.User(id)` enrute correctamente cuando los claims no se mapean.
- Endpoint `POST /auth/login` con auditoría completa (éxito, fallo
  por usuario desconocido, fallo por contraseña).
- Seed automático del usuario admin la primera vez que arranca el
  servidor, leyendo `EnterpriseChat:Bootstrap:AdminPassword` de la config.
- `ChatHub` con autenticación obligatoria, gate de licencia en
  `OnConnectedAsync`, registro/cierre de `Sessions` y método
  `SendDirectMessage` con persistencia y entrega a través de
  `IChatClient.OnMessageReceived`.
- `ConcurrentSessionCounter` que aplica el cap por **usuarios distintos
  conectados**, permitiendo varias ventanas por usuario sin consumir
  slots extra.
- Scripts de despliegue: `install-windows-service.ps1`,
  `uninstall-windows-service.ps1`, `enterprisechat.service` unit y
  `install-linux.sh`.
- Suite de tests ampliada: counter, login OK/KO, hub anonymous reject,
  DM 1:1 round-trip (22 tests, todos en verde).

### Added — Fase 0 (fundación)

- Solución `EnterpriseChat.sln` con proyectos `Server`, `Client`,
  `Protocol`, `Licensing.Abstractions` y `Tests`.
- Central Package Management (`Directory.Packages.props`).
- Pin del SDK .NET 8 vía `global.json`.
- Esqueleto del servidor: minimal host ASP.NET Core con SignalR,
  Serilog, `UseWindowsService` y `UseSystemd`, endpoints `/healthz`
  y `/license`, carga de plugins de licenciamiento desde `plugins/`.
- `FreeLicenseValidator` con cap de 10 usuarios concurrentes.
- 4 ADRs iniciales: open core, WPF, SignalR, BCrypt.
- Licencia AGPLv3 + documento de licenciamiento dual.
