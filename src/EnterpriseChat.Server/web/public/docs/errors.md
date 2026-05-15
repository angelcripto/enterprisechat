# Códigos de error

Todos los endpoints devuelven JSON. Los códigos relevantes:

## 400 — Bad Request

Validación de input. El cuerpo siempre contiene:

```json
{ "error": "Mensaje explicativo en español" }
```

Casos típicos:

- Campos obligatorios ausentes (`displayName`, `username`, …).
- Valores fuera de rango (`expiresInDays < 0`).
- Strings que exceden `MaxLength`.
- Enum con valor desconocido (rol distinto a `"User"` o `"Admin"`).

## 401 — Unauthorized

Token ausente, mal formado, caducado, no existe en BD o revocado.
**No incluye detalle** del motivo concreto para no filtrar información a
quien sondea — el log del servidor sí lo registra.

Causas comunes:

- Falta el header `Authorization: Bearer …`.
- JWT con firma inválida (cliente generó token con clave equivocada).
- JWT caducado (revisar TTL).
- PAT alterada (cambiar un carácter rompe el hash → no se encuentra).
- PAT revocada o caducada.

## 403 — Forbidden

Autenticado correctamente, pero el rol no llega:

- PAT con rol `User` accede a endpoint `/admin/*` → 403.
- Usuario `User` intenta endpoint admin → 403.
- Endpoint específico (p.ej. `POST /admin/users` cuando se ha alcanzado
  el cap de licencia) devuelve 403 con el motivo:

```json
{
  "error": "Edición Free: límite de 5 cuentas activas alcanzado (5 en uso). Actualiza a Pro o desactiva un usuario.",
  "currentActive": 5,
  "max": 5
}
```

## 404 — Not Found

Recurso inexistente. El cuerpo puede ser vacío o incluir `{ "error": "…" }`
según el endpoint. No se distingue *"no existe"* de *"existe pero no
tengo permiso"* en endpoints donde la enumeración filtraría información
(p.ej. detalles de mensajes en salas de las que no soy miembro).

## 409 — Conflict

Estado actual incompatible con la operación:

- `POST /admin/users` con un username ya existente.
- `POST /admin/api-keys/{id}/rotate` sobre una clave ya revocada
  (la respuesta del servidor incluye el mensaje del servicio).

## 422 — Unprocessable Entity

Solo aparece cuando una **PAT** intenta un endpoint que requiere un
userId numérico real (DMs, `/me/inbox`, `MarkAsRead`, etc.). Las claves
de servicio no tienen userId — es por diseño.

## 429 — Too Many Requests

Rate limit excedido. Sólo afecta a peticiones autenticadas con PAT
(60 req/min/clave). Cabecera obligatoria:

```http
HTTP/1.1 429 Too Many Requests
Retry-After: 60
```

Cliente debe esperar `Retry-After` segundos antes de reintentar. En
producción usa backoff exponencial: si fallas dos rondas, espera 120s, 240s…

## 500 — Internal Server Error

Bug del servidor o BD caída. El cuerpo es genérico y el detalle queda en
`logs/server-*.log`. Si recibes 500 reproducible, abre un issue con:

- método + ruta llamados
- request id si aparece en respuesta
- mismo timestamp del log del servidor

## Cabeceras útiles

| Header | Significado |
|---|---|
| `Retry-After` | segundos a esperar (429) |
| `WWW-Authenticate` | schemes aceptados (401), `Bearer realm="..."` |
| `Content-Type: application/problem+json` | RFC 7807 problem details (algunos endpoints) |
