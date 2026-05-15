# Guía rápida — EnterpriseChat API

Esta guía te lleva del cero a tu primera llamada autenticada en cinco minutos.

## 1. Pide acceso a un admin

Para usar la API necesitas un **PAT (Personal Access Token)**. Sólo un admin
del servidor de chat puede emitirlo:

1. Inicia sesión en el servidor como administrador (`https://<servidor>:5080`).
2. Sidebar → *Administración* → *API keys*.
3. Botón **Nueva clave**. Rellena nombre, rol (User o Admin) y caducidad
   opcional.
4. Copia el secreto **inmediatamente** (`ec_pat_…`). No se vuelve a mostrar.

> **Tip:** si pierdes el secreto, basta con *Rotar* la clave desde la misma
> UI. El admin no necesita borrar la integración existente — la rotación
> mantiene el mismo registro de audit y enlaza la nueva clave con la antigua.

## 2. Llama a un endpoint

Con el token en mano, todas las peticiones llevan `Authorization: Bearer …`.
Confirma que funciona con `GET /healthz`:

```bash
curl -H "Authorization: Bearer ec_pat_XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX" \
     https://<servidor>:5080/healthz
# → {"status":"ok"}
```

`/healthz` es público (no exige auth), pero la cabecera no estorba.
Para algo más útil, lista los usuarios visibles:

```bash
curl -H "Authorization: Bearer ec_pat_…" \
     https://<servidor>:5080/users
```

## 3. Explora la API completa

Abre `/docs/api` en el navegador (Swagger UI). Cada endpoint trae:

- método + ruta
- parámetros y body esperado
- esquemas de respuesta para 200/400/401/403/422
- botón **Try it out** (mete tu PAT arriba a la derecha → *Authorize*)

## 4. Buenas prácticas

- **Rate limit.** Las PAT están limitadas a **60 req/min** por clave. Cuando
  excedes, el servidor responde `429 Too Many Requests` con header
  `Retry-After: 60`. Backoff exponencial si el bot es agresivo.
- **Una clave por integración.** No reutilices el mismo PAT en dos servicios
  distintos — si tienes que revocar uno, no quieres tumbar el otro.
- **Rota periódicamente.** Calendariza una rotación cada 6 meses como
  máximo. La operación es inmediata y no requiere downtime: emites el
  nuevo, actualizas la integración, y revocas el viejo (o usas
  `graceSeconds` para una transición sin parpadeos).
- **Audit log.** Cada acción sobre claves (crear, rotar, revocar) deja
  rastro en la tabla `AuditLog` del servidor con el actor humano y la
  acción exacta. Si una clave se filtra, hay registro de cuándo se creó
  y por quién.

## ¿Y SignalR?

Las PAT **no** se aceptan en `/hubs/chat`. El hub asume que el usuario es
una persona con un userId numérico, y las claves de servicio no encajan en
ese modelo. Si tu integración necesita realtime (recibir mensajes según
llegan), tu bot debe loguearse como un usuario humano (`POST /auth/login`)
y usar el JWT en el query string `?access_token=…`. Detalles en
[signalr-hub.md](signalr-hub.md).
