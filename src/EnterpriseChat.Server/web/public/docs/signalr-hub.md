# Hub SignalR (`/hubs/chat`)

Canal bidireccional en tiempo real para mensajería, presencia, escritura
y notificaciones. **Sólo acepta JWT humano**, no PAT — el hub depende del
userId numérico del que llama para enrutar mensajes a las conexiones
correctas.

Para clientes custom que necesitan tiempo real, el bot tiene que
loguearse como un usuario humano (`POST /auth/login`) y conectar con ese
JWT.

## Conexión

WebSocket. El token va por **query string** (no header, porque el
handshake WebSocket de los navegadores no admite headers custom):

```
wss://<servidor>:5080/hubs/chat?access_token=<JWT>
```

Cliente JS oficial (Microsoft SignalR):

```ts
import { HubConnectionBuilder, HttpTransportType } from "@microsoft/signalr";

const conn = new HubConnectionBuilder()
    .withUrl("/hubs/chat", {
        accessTokenFactory: () => myJwt,
        transport: HttpTransportType.WebSockets,
    })
    .withAutomaticReconnect()
    .build();
```

Cualquier cliente que hable el protocolo de SignalR sirve — hay
implementaciones en .NET, JS, Java, Python (`signalrcore`), Go (`philippseith/signalr`).

## Métodos invocables (cliente → servidor)

| Método | Firma | Devuelve |
|---|---|---|
| `SendDirectMessage` | `(toUserId: int, body: string)` | `long` (serverId del mensaje) |
| `SendDirectMessageWithAttachment` | `(toUserId: int, body: string, attachmentId: long)` | `long` |
| `CreateRoom` | `(name: string, isPrivate: bool)` | `int` (roomId) |
| `JoinRoom` | `(roomId: int)` | — |
| `LeaveRoom` | `(roomId: int)` | — |
| `SendRoomMessage` | `(roomId: int, body: string)` | `long` |
| `SendRoomMessageWithAttachment` | `(roomId: int, body: string, attachmentId: long)` | `long` |
| `GetRoomHistory` | `(roomId: int, limit?: int = 50, beforeServerId?: long = MaxValue)` | `ChatMessage[]` |
| `GetDirectHistory` | `(peerUserId: int, limit?: int = 50, beforeServerId?: long = MaxValue)` | `ChatMessage[]` |
| `MarkAsRead` | `(serverId: long)` | — |
| `Typing` | `(toUserId?: int, roomId?: int)` | — (envía solo uno de los dos) |

Cuerpos de mensaje truncados a 4096 chars (`EnterpriseChat:Server:MaxMessageBodyLength`).
`SendRoomMessage` falla con `HubException("No eres miembro de esta sala")`
si el caller no está en `RoomMember`.

## Eventos suscribibles (servidor → cliente)

Métodos de `IChatClient` que el servidor invoca; tu cliente los registra
con `connection.On("nombreEvento", handler)`.

| Evento | Payload |
|---|---|
| `OnMessageReceived` | `ChatMessage` — DM o sala (mira `RoomId` para distinguir) |
| `OnPresenceChanged` | `(userId: int, isOnline: bool)` — broadcast a todos |
| `OnRoomMembershipChanged` | `(roomId: int, userId: int, joined: bool)` — al grupo de la sala |
| `OnMessageRead` | `(serverId: long, byUserId: int, readAt: DateTimeOffset)` |
| `OnTyping` | `(fromUserId: int, toUserId?: int, roomId?: int)` |
| `OnLicenseDenied` | `(reason: string)` — cap de licencia agotado, aborta conexión |
| `OnPinnedChanged` | `(roomId: int, messageId: long, pinned: bool)` |
| `OnReactionChanged` | `(messageId: long, userId: int, emoji: string, added: bool)` |

`OnMessageReceived` se dispara con el mensaje en cuanto otro usuario lo
manda — tanto si es un DM dirigido a ti (`ToUserId == yo`) como si es un
mensaje en una sala donde estás como miembro. El throttling de
`OnTyping` lo aplica el cliente WPF: TTL 3s, mínimo 2s entre eventos.

## Tipo `ChatMessage`

```ts
interface ChatMessage {
    serverId: long;
    fromUserId: number;
    toUserId: number | null;   // null en mensajes de sala
    roomId: number | null;     // null en DMs
    body: string;
    sentAt: string;            // ISO 8601 con offset
    readAt: string | null;     // se completa con MarkAsRead
    attachmentId: number | null;
    attachmentFileName: string | null;
    attachmentSizeBytes: number | null;
}
```

## Ejemplo mínimo (TypeScript)

```ts
import { HubConnectionBuilder } from "@microsoft/signalr";

const conn = new HubConnectionBuilder()
    .withUrl("/hubs/chat", { accessTokenFactory: () => jwt })
    .build();

conn.on("OnMessageReceived", (msg) => {
    console.log(`#${msg.serverId}: ${msg.fromUserId} → ${msg.body}`);
});

await conn.start();

// Mandar un DM
const id = await conn.invoke("SendDirectMessage", 7, "Hola Luis");

// Marcar como leído
await conn.invoke("MarkAsRead", id);
```
