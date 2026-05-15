# ADR 0005 — API pública con claves rotables (PAT) para clientes de terceros

- Fecha: 2026-05-16
- Estado: Aceptada

## Contexto

El servidor expone una API REST (`/auth`, `/users`, `/rooms`, `/search`,
`/files`, `/admin/*`, `/me/*`, …) y un hub SignalR (`/hubs/chat`) que hasta
ahora solo consumían los clientes oficiales: el WPF y el SPA Vue. Ambos se
autentican con un **JWT humano** de TTL corto emitido por
`POST /auth/login`.

Hay demanda de que developers externos construyan su propio cliente
(CLI, bot de turno, dashboard de monitoreo, integración con CI). Para eso
necesitan:

1. Una credencial **estable** que no caduque cada 60 minutos y que no
   obligue a almacenar usuario/contraseña en una integración.
2. Documentación pública de los endpoints.
3. Un modelo de permisos claro: qué puede y qué no puede hacer una
   integración.

Reutilizar el JWT humano para bots es mala práctica (credenciales de
persona en CI, sin forma de revocar sin cambiar la contraseña del
usuario). El estándar de facto para este caso son los **Personal Access
Tokens** estilo GitHub/GitLab.

Decisiones de diseño en juego (consultadas con el responsable del
producto antes de implementar):

- **Modelo de auth**: ¿PAT directa en `Authorization: Bearer` o PAT que
  se intercambia por un JWT corto (OAuth client-credentials)?
- **Quién emite claves**: ¿solo admins, cada usuario para sí mismo, o
  claves de servicio sin usuario?
- **Documentación**: ¿OpenAPI autogenerado, markdown manual, ambos?

## Decisión

**PAT directa**, opaca, con prefijo `ec_pat_`, presentada en
`Authorization: Bearer ec_pat_…` (y en `?api_key=…` para `/files` y
`/hubs`, mismo criterio que el fallback existente del JWT). Sólo se
persiste `SHA-256(token)`; el plaintext se entrega una única vez al
crear o rotar.

- **Service tokens, no impersonación.** Una PAT no representa a un
  usuario humano: su `sub` sintético es `apikey:<id>`. Lleva un rol
  (`User` o `Admin`) elegido al emitirla. Los endpoints que dependen de
  un userId numérico (DMs, `/me/inbox`, `/me/mentions`, marcado de
  leído, hub SignalR) la rechazan por diseño.
- **Sólo admins gestionan claves**, desde la única superficie admin del
  producto: el SPA Vue en `/manage/api-keys` y los endpoints
  `/admin/api-keys/*`. El cliente WPF no toca esto (ver
  [ADR-0002](0002-cliente-wpf-en-lugar-de-winui3.md) — el WPF es solo
  chat de usuario; toda admin vive en la web del server).
- **Combinación de schemes vía PolicyScheme**, no enumerando schemes en
  cada policy (ver Justificación).
- **OpenAPI autogenerado (Swashbuckle) + Swagger UI público + markdowns
  narrativos** servidos en `/docs/*`. El hub SignalR no se autodocumenta
  vía OpenAPI, así que su contrato vive en `docs/signalr-hub.md`.
- **Rate limit** de 60 req/min por clave (ventana fija); el JWT humano
  no se limita.

## Justificación

1. **PAT directa antes que PAT→JWT.** El intercambio (OAuth
   client-credentials) obliga al dev a orquestar refresh y duplica el
   pipeline. La PAT directa reaprovecha el pipeline de autorización
   existente: un `AuthenticationHandler` valida el token y construye un
   `ClaimsPrincipal` con los mismos nombres de claim que
   `JwtTokenIssuer`, así que `RequireRole`, `SubClaimUserIdProvider`,
   etc. funcionan sin tocarse.

2. **Service tokens antes que impersonación.** Permitir que una PAT
   "actúe como" un usuario concreto (`acting_as_user_id`) abre superficie
   de ataque (una PAT filtrada expone los DMs de alguien) y complica el
   modelo de permisos. El 95 % de las integraciones reales (bots de
   notificación, dashboards, automatización admin) no necesitan
   impersonar a nadie. Si surge el caso, se añade encima sin romper nada.

3. **PolicyScheme `JwtOrApiKey` como default.** El primer intento fue el
   camino "limpio": `AuthorizationPolicyBuilder(jwt, apikey)
   .RequireRole("Admin")`. Resultado: los endpoints admin devolvían 403
   con JWTs válidos. Causa: cuando una policy enumera dos schemes y uno
   devuelve `NoResult` (el handler de PAT cuando no hay token PAT), el
   merge de principals en `PolicyEvaluator` deja un compound principal
   donde `RequireRole` no resuelve el rol del JWT exitoso. Es un
   comportamiento de ASP.NET Core 8, reproducido con tests reales. El
   `PolicyScheme` lo rodea: mira el header/query, reenvía a JwtBearer o
   a ApiKey, y la policy ve siempre un único principal.

4. **El hub SignalR queda fuera del MVP de PAT.** `ChatHub` asume
   `Context.User` ↔ usuario real con userId numérico para enrutar
   mensajes. Las claves de servicio no encajan. En lugar de dejarlo
   explotar dentro de `GetUserId()`, el hub fija scheme explícito a
   JwtBearer y rechaza la PAT en el handshake con 401. Un bot que
   necesite realtime se loguea como usuario humano.

5. **Swashbuckle.** Es el estándar de la comunidad .NET con Swagger UI
   integrado; `Microsoft.AspNetCore.OpenApi` (built-in) sólo genera spec
   sin UI. La descripción del API y de los security schemes se escribe
   en markdown que Swagger UI renderiza, así la spec sirve a la vez de
   doc narrativa.

## Consecuencias

Positivas:

- Integraciones de terceros sin compartir credenciales humanas, con
  rotación/revocación de un click y audit log por acción.
- El pipeline de autorización no se duplicó: un solo `PolicyScheme`
  decide; las policies existentes no se tocaron salvo para nombrar
  `AdminOnly`.
- Documentación pública versionada con el código (OpenAPI + markdowns
  en `web/public/docs/`, copiados a `wwwroot/docs/` por Vite).

Negativas / a vigilar:

- **`[FromForm] IFormFile` fuera de Swagger UI.** Swashbuckle 6.x peta
  al inferir esos endpoints en minimal APIs (`POST /files`,
  `POST /users/me/avatar`). Se excluyen del spec vía
  `DocInclusionPredicate` y se documentan a mano en
  `getting-started.md`. Deuda: implementar un `IOperationFilter`
  completo o migrar a `IFormCollection` si se quieren en la UI.
- **Rate limit en memoria.** El bucket de 60 req/min vive en el proceso
  (`PartitionedRateLimiter`). Si algún día el server escala a varias
  instancias detrás de un balanceador, el límite efectivo se multiplica
  por el número de instancias. El despliegue actual es monoinstancia
  on-premise, así que es aceptable; revisar si llega HA.
- **`GenerateDocumentationFile=true`** elevó a error 1 comentario XML
  roto preexistente (CS1734 en `MySqlAuthProvider`) y obligó a suprimir
  CS1591 a nivel csproj (el `.editorconfig` lo apaga demasiado tarde
  para `TreatWarningsAsErrors`). Cualquier `<paramref>` huérfano nuevo
  ahora rompe el build — es un efecto deseable, pero hay que saberlo.
- Cambios futuros en el contrato de la API son ahora **superficie
  pública** observada por clientes de terceros: romperla obliga a
  versionar (`/docs/openapi/v2.json`) en lugar de cambiar libremente.

## Plan de revisión

- Si el server pasa a multiinstancia: mover el rate limiter a un store
  distribuido (Redis) o aceptar el límite por-instancia documentándolo.
- Reevaluar el modo `acting_as_user_id` sólo si una integración real lo
  pide (bot que manda DMs como un humano).
- Resolver la deuda de `IFormFile` en Swagger cuando alguien necesite
  probar uploads desde la UI.
