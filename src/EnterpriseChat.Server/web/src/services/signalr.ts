import * as signalR from "@microsoft/signalr";
import type { ChatMessage } from "@/api/types";
import { getAccessToken } from "@/api/client";

/**
 * Thin wrapper around HubConnection that mirrors the contract exposed by
 * `EnterpriseChat.Server/Hubs/ChatHub.cs`. Anything inside the app talks to
 * SignalR through this service so the rest of the code never imports
 * @microsoft/signalr directly.
 *
 * The hub identifies users via the `sub` claim of the JWT, which the server
 * reads in `SubClaimUserIdProvider`. Auth happens via `?access_token=...` on
 * the WebSocket upgrade, because browsers don't allow custom headers on the
 * WS handshake.
 */
export type ChatHubEventMap = {
    OnMessageReceived: (m: ChatMessage) => void;
    OnPresenceChanged: (userId: number, isOnline: boolean) => void;
    OnRoomMembershipChanged: (roomId: number, userId: number, joined: boolean) => void;
    OnMessageRead: (serverId: number, byUserId: number, readAt: string) => void;
    OnTyping: (fromUserId: number, toUserId: number | null, roomId: number | null) => void;
    OnLicenseDenied: (reason: string) => void;
    OnReactionChanged: (messageId: number, userId: number, emoji: string, added: boolean) => void;
    OnPinnedChanged: (roomId: number, messageId: number, pinned: boolean) => void;
};

class ChatHubService {
    private connection: signalR.HubConnection | null = null;

    isConnected(): boolean {
        return this.connection?.state === signalR.HubConnectionState.Connected;
    }

    async start(): Promise<void> {
        if (this.connection !== null && this.connection.state !== signalR.HubConnectionState.Disconnected) {
            return;
        }
        this.connection = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/chat", {
                accessTokenFactory: () => getAccessToken() ?? "",
            })
            .withAutomaticReconnect([0, 2000, 5000, 15000, 30000])
            .configureLogging(signalR.LogLevel.Information)
            .build();
        await this.connection.start();
    }

    async stop(): Promise<void> {
        if (this.connection !== null) {
            await this.connection.stop();
            this.connection = null;
        }
    }

    on<K extends keyof ChatHubEventMap>(event: K, handler: ChatHubEventMap[K]): void {
        this.requireConnection().on(event, handler as (...args: unknown[]) => void);
    }

    off<K extends keyof ChatHubEventMap>(event: K, handler: ChatHubEventMap[K]): void {
        this.requireConnection().off(event, handler as (...args: unknown[]) => void);
    }

    // -- Client → server methods (must match ChatHub signatures exactly). --

    sendDirectMessage(toUserId: number, body: string): Promise<number> {
        return this.invoke<number>("SendDirectMessage", toUserId, body);
    }

    sendDirectMessageWithAttachment(toUserId: number, body: string, attachmentId: number): Promise<number> {
        return this.invoke<number>("SendDirectMessageWithAttachment", toUserId, body, attachmentId);
    }

    createRoom(name: string, isPrivate: boolean): Promise<number> {
        return this.invoke<number>("CreateRoom", name, isPrivate);
    }

    joinRoom(roomId: number): Promise<void> {
        return this.invoke<void>("JoinRoom", roomId);
    }

    leaveRoom(roomId: number): Promise<void> {
        return this.invoke<void>("LeaveRoom", roomId);
    }

    sendRoomMessage(roomId: number, body: string): Promise<number> {
        return this.invoke<number>("SendRoomMessage", roomId, body);
    }

    sendRoomMessageWithAttachment(roomId: number, body: string, attachmentId: number): Promise<number> {
        return this.invoke<number>("SendRoomMessageWithAttachment", roomId, body, attachmentId);
    }

    getRoomHistory(roomId: number, limit = 50, beforeServerId = Number.MAX_SAFE_INTEGER): Promise<ChatMessage[]> {
        return this.invoke<ChatMessage[]>("GetRoomHistory", roomId, limit, beforeServerId);
    }

    getDirectHistory(peerUserId: number, limit = 50, beforeServerId = Number.MAX_SAFE_INTEGER): Promise<ChatMessage[]> {
        return this.invoke<ChatMessage[]>("GetDirectHistory", peerUserId, limit, beforeServerId);
    }

    markAsRead(serverId: number): Promise<void> {
        return this.invoke<void>("MarkAsRead", serverId);
    }

    typing(toUserId: number | null, roomId: number | null): Promise<void> {
        return this.invoke<void>("Typing", toUserId, roomId);
    }

    private invoke<T>(method: string, ...args: unknown[]): Promise<T> {
        return this.requireConnection().invoke<T>(method, ...args);
    }

    private requireConnection(): signalR.HubConnection {
        if (this.connection === null) {
            throw new Error("ChatHubService no iniciado. Llama a start() después del login.");
        }
        return this.connection;
    }
}

export const chatHub = new ChatHubService();
