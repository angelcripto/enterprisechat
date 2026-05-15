# Autenticación

El servidor acepta **dos vías equivalentes** para cualquier endpoint
anotado con candado. El pipeline las distingue mirando el prefijo del
token en `Authorization: Bearer …`.

## JWT humano

Para clientes que actúan en nombre de una persona (apps de escritorio
custom, dashboards con login propio):

```bash
curl -X POST https://<servidor>:5080/auth/login \
     -H "Content-Type: application/json" \
     -d '{"username":"admin","password":"<password>"}'
```

Respuesta:

```json
{
  "accessToken": "eyJhbGciOi…",
  "expiresAt": "2026-05-15T12:34:56Z",
  "userId": 2,
  "username": "admin",
  "role": "Admin"
}
```

El JWT:

- es válido durante el TTL configurado en el server (`EnterpriseChat:Jwt:AccessTokenLifetimeMinutes`, 60 min por defecto).
- lleva los claims `sub` (userId), `name` (username), `role`, `jti`.
- se valida con HMAC-SHA256 contra la clave del server.
- es **el único token aceptado por el hub SignalR** en `/hubs/chat`.

## Personal Access Tokens (PAT)

Para integraciones de servicio (bots, CI, dashboards de monitoreo). El
admin las crea desde *Administración → API keys* o vía `POST /admin/api-keys`.

### Formato

```
ec_pat_<43-chars-base64url>
```

Los 11 primeros caracteres (`ec_pat_XYZW`) son el **prefijo público** que
aparece en logs y en la UI de gestión — útil para identificar qué clave
es sin filtrar el secreto. El resto sólo se ve **una vez**, al crearla
o rotarla; el servidor solo almacena `SHA-256(token)`.

### Uso

Igual que un JWT:

```bash
curl -H "Authorization: Bearer ec_pat_…" https://<servidor>:5080/users
```

**Excepción**: en `GET /files/{id}` (descargas via `<a href>`) y en
endpoints donde el header no se puede poner, también se acepta
`?api_key=ec_pat_…` como query string.

### Diferencias respecto a un JWT

| Aspecto | JWT humano | PAT |
|---|---|---|
| TTL típico | 60 min | sin caducar o configurable |
| Renovación | re-login | rotación manual desde la UI |
| Rate limit | sin límite del middleware | **60 req/min por clave** |
| Acepta hub SignalR | sí | no |
| Endpoints que dependen del userId (`/me/inbox`, `/me/mentions`, `/me/saved`, DMs por SignalR, marcado de leído) | sí | responden 4xx |
| Endpoints admin (`/admin/*`) | sí si rol Admin | sí si la PAT se emitió con rol Admin |

### Rotación

Una rotación crea una clave **nueva** con el mismo nombre/notas/rol y
marca la antigua como revocada. Audit-friendly: el campo `RotatedFromId`
de la nueva fila apunta a la anterior, así que la cadena completa de
versiones queda en BD.

```bash
curl -X POST https://<servidor>:5080/admin/api-keys/42/rotate \
     -H "Authorization: Bearer <JWT-admin>" \
     -H "Content-Type: application/json" \
     -d '{"graceSeconds": 60}'
```

El parámetro `graceSeconds` (opcional, default 0) marca la clave anterior
para revocarse `N` segundos después. Útil para despliegues sin parpadeos:
emites la nueva, das 60s para actualizar variables de entorno, y la
vieja muere sola.

### Revocación

Marca la clave como revocada y queda inutilizable inmediatamente. No se
borra físicamente — la fila se conserva con `RevokedAt` y `RevokeReason`
para audit:

```bash
curl -X POST https://<servidor>:5080/admin/api-keys/42/revoke \
     -H "Authorization: Bearer <JWT-admin>" \
     -H "Content-Type: application/json" \
     -d '{"reason": "filtrada en repo público"}'
```

`DELETE /admin/api-keys/{id}` es un alias de revoke.

## Rate limiting

Cada PAT tiene su propio bucket fijo de **60 peticiones por minuto**. Al
exceder, el servidor responde:

```http
HTTP/1.1 429 Too Many Requests
Retry-After: 60

```

Las peticiones autenticadas con JWT humano no están limitadas por
nuestro middleware (asumimos que un humano no martillea el servidor a
1k RPS). En la práctica el cuello de botella lo pone Kestrel.

## Errores comunes

| Status | Significado |
|---|---|
| 401 | Token ausente, mal formado, caducado o no existe |
| 403 | Token autenticado, pero el rol no llega para este endpoint |
| 422 | Endpoint que necesita un userId humano y la PAT no lo tiene |
| 429 | Rate limit excedido — `Retry-After` en segundos |

Más detalle en [errors.md](errors.md).
