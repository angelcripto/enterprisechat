# ADR 0003 — Transporte SignalR (no `System.Net.WebSockets` puro)

- Fecha: 2026-05-13
- Estado: Aceptada

## Contexto

El servidor debe atender hasta cientos de clientes concurrentes en LAN o
detrás de un reverse proxy a internet, manteniendo:

- Conexión persistente bidireccional.
- Reconexión transparente tras corte de red o suspensión del cliente.
- Broadcast a salas / canales y a usuarios concretos.
- Tipado fuerte de los mensajes en ambos extremos (cliente y servidor C#).

Opciones consideradas:

1. **WebSockets puros** (`System.Net.WebSockets.WebSocket` + Kestrel
   `app.UseWebSockets`) con protocolo wire propio (JSON o MessagePack).
2. **SignalR** (Microsoft.AspNetCore.SignalR), construido sobre
   WebSockets / SSE / long-polling con negociación automática.
3. **gRPC streaming** (HTTP/2). Excelente para servicios; engorroso para
   clientes WPF y poco amistoso a través de reverse proxies legacy.

## Decisión

Se elige **SignalR**.

## Justificación

1. **Reconexión automática con state**: `HubConnection` reintenta con
   backoff y conserva la suscripción a grupos. Implementar esto sobre
   WebSockets puros es trabajo no diferenciador.
2. **Grupos == canales**: `Groups.AddToGroupAsync` / `Clients.Group(...)`
   se mapean directamente a salas y canales del chat. Saltarse el
   framework implicaría reimplementar este registro.
3. **Tipado fuerte cross-process**: hub tipado con `Hub<IChatClient>` y
   `IChatHub` definidos en el ensamblado `Protocol`, compartidos entre
   server y client por referencia. Compile-time safety en ambos lados.
4. **Fallback a long-polling** para clientes detrás de proxies/firewalls
   que no soportan WebSockets bien (lugares reales en pymes).
5. **Coste**: dependencia de ASP.NET Core (ya asumida para Kestrel + TLS +
   minimal APIs) y formato wire ligeramente más verbose (`target`,
   `arguments`). No es bloqueante para nuestros volúmenes objetivo.

## Consecuencias

Positivas:

- Menos código propio que mantener y depurar.
- Compatibilidad probada con clientes JavaScript/Java/Swift si en el
  futuro hay app web o móvil.
- Authentication via JWT bearer en la negociación `/hubs/chat?access_token=…`
  ya soportada por la integración SignalR + JwtBearer.

Negativas:

- Protocolo wire algo más verbose; documentación pública del wire format
  es más ligera que la de un WebSocket personalizado.
- Cualquier proxy intermedio debe soportar SignalR (cabeceras `Connection`,
  upgrades). En la práctica nginx/Caddy/IIS lo soportan sin tocar nada.

## Alternativas descartadas

- WebSockets puros: trabajo significativo replicando lo que SignalR ya da,
  sin valor diferencial.
- gRPC: poco encaje con cliente WPF, fricción adicional con reverse proxies
  tradicionales y curva de aprendizaje innecesaria para este dominio.
