using System.Globalization;
using System.Security.Claims;
using EnterpriseChat.Protocol;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Data.Entities;
using EnterpriseChat.Server.Licensing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseChat.Server.Hubs;

// El hub asume que Context.User representa a un usuario humano con
// userId numérico; las claves de servicio (PAT) no encajan, así que
// fijamos el scheme a JWT explícitamente para que un PAT presentado
// vía ?api_key= reciba 401 en el handshake en lugar de explotar más
// tarde en GetUserId().
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ChatHub(
    IDbContextFactory<ChatDbContext> dbFactory,
    ConcurrentSessionCounter sessionCounter,
    ILogger<ChatHub> log) : Hub<IChatClient>
{
    private const int MaxBodyLength = 4096;

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        if (userId is null)
        {
            log.LogWarning("Conexión rechazada: sin claim de usuario.");
            Context.Abort();
            return;
        }

        var admission = sessionCounter.TryAdmit(userId.Value);
        if (!admission.Admitted)
        {
            await Clients.Caller.OnLicenseDenied(admission.DeniedReason ?? "Licencia agotada.");
            log.LogWarning(
                "Conexión rechazada por licencia: usuario {UserId} ({Reason}).",
                userId,
                admission.DeniedReason);
            Context.Abort();
            return;
        }

        await using (var db = await dbFactory.CreateDbContextAsync(Context.ConnectionAborted))
        {
            db.Sessions.Add(new Session
            {
                UserId = userId.Value,
                ConnectionId = Context.ConnectionId,
                ConnectedAt = DateTimeOffset.UtcNow,
                ClientIp = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString(),
                ClientVersion = Context.GetHttpContext()?.Request.Headers["X-Client-Version"].ToString()
            });
            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = userId.Value,
                Action = "session.start",
                Target = Context.ConnectionId
            });
            await db.SaveChangesAsync(Context.ConnectionAborted);
        }

        await base.OnConnectedAsync();

        // Subscribe this connection to every SignalR group corresponding to a room the user belongs to.
        await using (var db = await dbFactory.CreateDbContextAsync(Context.ConnectionAborted))
        {
            var memberships = await db.RoomMembers
                .Where(m => m.UserId == userId.Value)
                .Select(m => m.RoomId)
                .ToListAsync(Context.ConnectionAborted);
            foreach (var roomId in memberships)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroupName(roomId), Context.ConnectionAborted);
            }
        }

        if (admission.IsFirstConnection)
        {
            await Clients.Others.OnPresenceChanged(userId.Value, isOnline: true);
        }

        // Tell the caller who is currently online so its sidebar is correct on first paint.
        foreach (var onlineUserId in sessionCounter.Snapshot())
        {
            if (onlineUserId != userId.Value)
            {
                await Clients.Caller.OnPresenceChanged(onlineUserId, isOnline: true);
            }
        }

        log.LogInformation("Conexión aceptada: usuario {UserId}, connection {Cid}.", userId, Context.ConnectionId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        if (userId is not null)
        {
            var release = sessionCounter.Release(userId.Value);

            await using var db = await dbFactory.CreateDbContextAsync();
            var session = await db.Sessions
                .Where(s => s.ConnectionId == Context.ConnectionId && s.DisconnectedAt == null)
                .FirstOrDefaultAsync();
            if (session is not null)
            {
                session.DisconnectedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }

            if (release.WasLastConnection)
            {
                await Clients.Others.OnPresenceChanged(userId.Value, isOnline: false);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    public Task<long> SendDirectMessage(int toUserId, string body) =>
        SendDirectMessageCoreAsync(toUserId, body, attachmentId: null);

    public Task<long> SendDirectMessageWithAttachment(int toUserId, string body, long attachmentId) =>
        SendDirectMessageCoreAsync(toUserId, body, attachmentId);

    private async Task<long> SendDirectMessageCoreAsync(int toUserId, string body, long? attachmentId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(toUserId);

        body ??= string.Empty;
        if (string.IsNullOrWhiteSpace(body) && attachmentId is null)
        {
            throw new HubException("El mensaje no puede estar vacío.");
        }
        if (body.Length > MaxBodyLength)
        {
            throw new HubException($"El mensaje supera el máximo de {MaxBodyLength} caracteres.");
        }

        var fromUserId = GetUserId()
            ?? throw new HubException("Sesión sin identidad.");

        await using var db = await dbFactory.CreateDbContextAsync();

        var recipientExists = await db.Users.AnyAsync(u => u.Id == toUserId && u.IsActive);
        if (!recipientExists)
        {
            throw new HubException("Destinatario no encontrado.");
        }

        Attachment? attachment = null;
        if (attachmentId is long attId)
        {
            attachment = await db.Attachments.FindAsync(attId);
            if (attachment is null || attachment.UploadedByUserId != fromUserId)
            {
                throw new HubException("Adjunto no válido.");
            }
        }

        var entity = new Message
        {
            FromUserId = fromUserId,
            ToUserId = toUserId,
            Body = body,
            SentAt = DateTimeOffset.UtcNow,
            AttachmentId = attachment?.Id
        };
        db.Messages.Add(entity);
        await db.SaveChangesAsync();

        var wire = new ChatMessage(
            MessageId: Guid.NewGuid(),
            ServerId: entity.Id,
            FromUserId: fromUserId,
            ToUserId: toUserId,
            RoomId: null,
            Body: body,
            SentAt: entity.SentAt,
            AttachmentId: attachment?.Id,
            AttachmentFileName: attachment?.FileName,
            AttachmentSizeBytes: attachment?.SizeBytes);

        await Clients.User(toUserId.ToString(CultureInfo.InvariantCulture)).OnMessageReceived(wire);
        await Clients.User(fromUserId.ToString(CultureInfo.InvariantCulture)).OnMessageReceived(wire);

        entity.DeliveredAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        return entity.Id;
    }

    public async Task<int> CreateRoom(string name, bool isPrivate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 64)
        {
            throw new HubException("Nombre demasiado largo (máx 64).");
        }

        var meId = GetUserId() ?? throw new HubException("Sesión sin identidad.");

        await using var db = await dbFactory.CreateDbContextAsync();

        var room = new Room
        {
            Name = name.Trim(),
            IsPrivate = isPrivate,
            CreatedByUserId = meId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();

        db.RoomMembers.Add(new RoomMember
        {
            RoomId = room.Id,
            UserId = meId,
            JoinedAt = DateTimeOffset.UtcNow
        });
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = meId,
            Action = "room.create",
            Target = room.Name
        });
        await db.SaveChangesAsync();

        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroupName(room.Id));
        return room.Id;
    }

    public async Task JoinRoom(int roomId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(roomId);
        var meId = GetUserId() ?? throw new HubException("Sesión sin identidad.");

        await using var db = await dbFactory.CreateDbContextAsync();
        var room = await db.Rooms.FindAsync(roomId)
            ?? throw new HubException("Sala no encontrada.");

        var alreadyMember = await db.RoomMembers.AnyAsync(m => m.RoomId == roomId && m.UserId == meId);
        if (!alreadyMember)
        {
            db.RoomMembers.Add(new RoomMember { RoomId = roomId, UserId = meId });
            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = meId,
                Action = "room.join",
                Target = room.Name
            });
            await db.SaveChangesAsync();
            await Clients.Group(RoomGroupName(roomId)).OnRoomMembershipChanged(roomId, meId, joined: true);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroupName(roomId));
    }

    public async Task LeaveRoom(int roomId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(roomId);
        var meId = GetUserId() ?? throw new HubException("Sesión sin identidad.");

        await using var db = await dbFactory.CreateDbContextAsync();
        var member = await db.RoomMembers.FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == meId);
        if (member is not null)
        {
            db.RoomMembers.Remove(member);
            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = meId,
                Action = "room.leave",
                Target = roomId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
            await db.SaveChangesAsync();
            await Clients.Group(RoomGroupName(roomId)).OnRoomMembershipChanged(roomId, meId, joined: false);
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomGroupName(roomId));
    }

    public Task<long> SendRoomMessage(int roomId, string body) =>
        SendRoomMessageCoreAsync(roomId, body, attachmentId: null);

    public Task<long> SendRoomMessageWithAttachment(int roomId, string body, long attachmentId) =>
        SendRoomMessageCoreAsync(roomId, body, attachmentId);

    private async Task<long> SendRoomMessageCoreAsync(int roomId, string body, long? attachmentId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(roomId);
        body ??= string.Empty;
        if (string.IsNullOrWhiteSpace(body) && attachmentId is null)
        {
            throw new HubException("El mensaje no puede estar vacío.");
        }
        if (body.Length > MaxBodyLength)
        {
            throw new HubException($"El mensaje supera el máximo de {MaxBodyLength} caracteres.");
        }

        var meId = GetUserId() ?? throw new HubException("Sesión sin identidad.");

        await using var db = await dbFactory.CreateDbContextAsync();

        var isMember = await db.RoomMembers.AnyAsync(m => m.RoomId == roomId && m.UserId == meId);
        if (!isMember)
        {
            throw new HubException("No eres miembro de esa sala.");
        }

        Attachment? attachment = null;
        if (attachmentId is long attId)
        {
            attachment = await db.Attachments.FindAsync(attId);
            if (attachment is null || attachment.UploadedByUserId != meId)
            {
                throw new HubException("Adjunto no válido.");
            }
        }

        var entity = new Message
        {
            FromUserId = meId,
            ToUserId = null,
            RoomId = roomId,
            Body = body,
            SentAt = DateTimeOffset.UtcNow,
            AttachmentId = attachment?.Id
        };
        db.Messages.Add(entity);
        await db.SaveChangesAsync();

        var wire = new ChatMessage(
            MessageId: Guid.NewGuid(),
            ServerId: entity.Id,
            FromUserId: meId,
            ToUserId: null,
            RoomId: roomId,
            Body: body,
            SentAt: entity.SentAt,
            AttachmentId: attachment?.Id,
            AttachmentFileName: attachment?.FileName,
            AttachmentSizeBytes: attachment?.SizeBytes);

        await Clients.Group(RoomGroupName(roomId)).OnMessageReceived(wire);

        entity.DeliveredAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        return entity.Id;
    }

    public async Task<IReadOnlyCollection<ChatMessage>> GetRoomHistory(int roomId, int limit = 50, long beforeServerId = long.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(roomId);
        if (limit is <= 0 or > 200)
        {
            limit = 50;
        }

        var meId = GetUserId() ?? throw new HubException("Sesión sin identidad.");

        await using var db = await dbFactory.CreateDbContextAsync();
        var isMember = await db.RoomMembers.AnyAsync(m => m.RoomId == roomId && m.UserId == meId);
        if (!isMember)
        {
            throw new HubException("No eres miembro de esa sala.");
        }

        var rows = await db.Messages
            .Where(m => m.RoomId == roomId && m.Id < beforeServerId)
            .OrderByDescending(m => m.Id)
            .Take(limit)
            .Select(m => new
            {
                m.Id,
                m.FromUserId,
                m.ToUserId,
                m.RoomId,
                m.Body,
                m.SentAt,
                m.AttachmentId,
                AttachmentFileName = m.Attachment != null ? m.Attachment.FileName : null,
                AttachmentSizeBytes = m.Attachment != null ? (long?)m.Attachment.SizeBytes : null
            })
            .ToListAsync();

        var messages = rows.Select(m => new ChatMessage(
            MessageId: Guid.Empty,
            ServerId: m.Id,
            FromUserId: m.FromUserId,
            ToUserId: m.ToUserId,
            RoomId: m.RoomId,
            Body: m.Body,
            SentAt: m.SentAt,
            AttachmentId: m.AttachmentId,
            AttachmentFileName: m.AttachmentFileName,
            AttachmentSizeBytes: m.AttachmentSizeBytes)).ToList();
        messages.Reverse();
        return messages;
    }

    public async Task MarkAsRead(long serverId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(serverId);
        var meId = GetUserId() ?? throw new HubException("Sesión sin identidad.");

        await using var db = await dbFactory.CreateDbContextAsync();
        var msg = await db.Messages.FindAsync(serverId);
        if (msg is null)
        {
            return;
        }

        // Only the recipient (DM) or any room member (room) can mark as read.
        var recipientAllowed =
            (msg.ToUserId == meId)
            || (msg.RoomId is int rid && await db.RoomMembers.AnyAsync(m => m.RoomId == rid && m.UserId == meId));
        if (!recipientAllowed)
        {
            return;
        }

        if (msg.ReadAt is null)
        {
            msg.ReadAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var notifyAt = msg.ReadAt!.Value;
        if (msg.RoomId is int roomId2)
        {
            await Clients.Group(RoomGroupName(roomId2)).OnMessageRead(serverId, meId, notifyAt);
        }
        else
        {
            // Notify both endpoints of the DM (so the sender sees read-by-recipient).
            await Clients.User(msg.FromUserId.ToString(CultureInfo.InvariantCulture))
                .OnMessageRead(serverId, meId, notifyAt);
            await Clients.User(meId.ToString(CultureInfo.InvariantCulture))
                .OnMessageRead(serverId, meId, notifyAt);
        }
    }

    /// <summary>
    /// Avisa de que el usuario ha empezado / sigue escribiendo. El emisor lo
    /// repite con throttle; el receptor lo oculta solo tras ~3,5s de silencio.
    /// </summary>
    public async Task Typing(int? toUserId, int? roomId)
    {
        var meId = GetUserId() ?? throw new HubException("Sesión sin identidad.");
        if (toUserId is null && roomId is null)
        {
            return;
        }
        if (toUserId is int peerId)
        {
            await Clients.User(peerId.ToString(CultureInfo.InvariantCulture))
                .OnTyping(meId, peerId, null);
        }
        else if (roomId is int rid)
        {
            await Clients.GroupExcept(RoomGroupName(rid), Context.ConnectionId)
                .OnTyping(meId, null, rid);
        }
    }

    /// <summary>
    /// Avisa de que el usuario ha DEJADO de escribir (envió el mensaje o vació el
    /// cuadro), para retirar el indicador al instante en vez de esperar a que
    /// expire el temporizador del receptor.
    ///
    /// Va como método y callback SEPARADOS en lugar de añadir un `bool isTyping`
    /// a <see cref="Typing"/> / <c>OnTyping</c>: el cliente WPF registra
    /// <c>On&lt;int, int?, int?&gt;(nameof(IChatClient.OnTyping), …)</c>, de 3
    /// argumentos. Si el servidor empezara a mandar 4, SignalR no rompería la
    /// compilación — fallaría al enlazar los argumentos en EJECUCIÓN
    /// ("provides 4 argument(s) but target expects 3"), que es peor porque pasa
    /// en silencio. Un callback nuevo que el WPF no registra simplemente se
    /// ignora, igual que ya ocurre con OnPinnedChanged y OnReactionChanged.
    /// </summary>
    public async Task TypingStopped(int? toUserId, int? roomId)
    {
        var meId = GetUserId() ?? throw new HubException("Sesión sin identidad.");
        if (toUserId is null && roomId is null)
        {
            return;
        }
        if (toUserId is int peerId)
        {
            await Clients.User(peerId.ToString(CultureInfo.InvariantCulture))
                .OnTypingStopped(meId, peerId, null);
        }
        else if (roomId is int rid)
        {
            await Clients.GroupExcept(RoomGroupName(rid), Context.ConnectionId)
                .OnTypingStopped(meId, null, rid);
        }
    }

    public async Task<IReadOnlyCollection<ChatMessage>> GetDirectHistory(int peerUserId, int limit = 50, long beforeServerId = long.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(peerUserId);
        if (limit is <= 0 or > 200)
        {
            limit = 50;
        }

        var meId = GetUserId() ?? throw new HubException("Sesión sin identidad.");

        await using var db = await dbFactory.CreateDbContextAsync();

        var rows = await db.Messages
            .Where(m =>
                m.RoomId == null
                && m.Id < beforeServerId
                && ((m.FromUserId == meId && m.ToUserId == peerUserId)
                    || (m.FromUserId == peerUserId && m.ToUserId == meId)))
            .OrderByDescending(m => m.Id)
            .Take(limit)
            .Select(m => new
            {
                m.Id,
                m.FromUserId,
                m.ToUserId,
                m.RoomId,
                m.Body,
                m.SentAt,
                m.AttachmentId,
                AttachmentFileName = m.Attachment != null ? m.Attachment.FileName : null,
                AttachmentSizeBytes = m.Attachment != null ? (long?)m.Attachment.SizeBytes : null
            })
            .ToListAsync();

        var messages = rows
            .Select(m => new ChatMessage(
                MessageId: Guid.Empty,
                ServerId: m.Id,
                FromUserId: m.FromUserId,
                ToUserId: m.ToUserId,
                RoomId: m.RoomId,
                Body: m.Body,
                SentAt: m.SentAt,
                AttachmentId: m.AttachmentId,
                AttachmentFileName: m.AttachmentFileName,
                AttachmentSizeBytes: m.AttachmentSizeBytes))
            .ToList();

        // Return chronological order for the client to render directly.
        messages.Reverse();
        return messages;
    }

    private int? GetUserId()
    {
        var raw = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Context.User?.FindFirstValue("sub");
        return int.TryParse(raw, CultureInfo.InvariantCulture, out var id) ? id : null;
    }

    /// <summary>SignalR group name for a room. Public so endpoints outside
    /// the hub class (engagement endpoints, etc.) can broadcast to it.</summary>
    public static string RoomGroupName(int roomId)
        => $"room:{roomId.ToString(CultureInfo.InvariantCulture)}";
}
