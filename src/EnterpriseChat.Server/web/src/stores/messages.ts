import { defineStore } from "pinia";
import { reactive, ref } from "vue";
import { chatHub } from "@/services/signalr";
import type { ChatMessage, ThreadKey } from "@/api/types";
import { threadKeyId } from "@/api/types";

/**
 * Messages indexed by thread (DM peer or room). For every thread we keep the
 * messages sorted ascending by serverId (with optimistic local messages at
 * the tail until the server assigns an id).
 */
export const useMessagesStore = defineStore("messages", () => {
    const byThread = reactive<Record<string, ChatMessage[]>>({});
    const loadingThreads = reactive<Record<string, boolean>>({});
    const typingByThread = reactive<Record<string, Set<number>>>({});
    const readCursors = reactive<Record<string, number>>({});
    const currentUserId = ref<number | null>(null);

    function setCurrentUser(id: number): void {
        currentUserId.value = id;
    }

    function get(key: ThreadKey): ChatMessage[] {
        return byThread[threadKeyId(key)] ?? [];
    }

    async function loadHistory(key: ThreadKey): Promise<void> {
        const id = threadKeyId(key);
        loadingThreads[id] = true;
        try {
            const history = key.kind === "room"
                ? await chatHub.getRoomHistory(key.roomId)
                : await chatHub.getDirectHistory(key.peerUserId);
            byThread[id] = [...history].sort(sortAsc);
        } finally {
            loadingThreads[id] = false;
        }
    }

    async function sendText(key: ThreadKey, body: string): Promise<void> {
        if (body.trim() === "") return;
        if (key.kind === "room") {
            await chatHub.sendRoomMessage(key.roomId, body);
        } else {
            await chatHub.sendDirectMessage(key.peerUserId, body);
        }
        // The hub echoes through OnMessageReceived; we don't insert locally.
    }

    function applyIncoming(message: ChatMessage): void {
        const key: ThreadKey = message.roomId !== null && message.roomId !== undefined
            ? { kind: "room", roomId: message.roomId }
            : { kind: "dm", peerUserId: directPeer(message) };
        const id = threadKeyId(key);
        const bucket = byThread[id] ?? (byThread[id] = []);
        if (message.serverId !== undefined && message.serverId !== null
            && bucket.some((m) => m.serverId === message.serverId)) {
            return; // duplicate, ignore
        }
        bucket.push(message);
        bucket.sort(sortAsc);
    }

    function applyMessageRead(serverId: number, byUserId: number): void {
        // Record latest read serverId per user; UI can use it to render double-tick.
        const k = `u:${byUserId}`;
        const existing = readCursors[k] ?? 0;
        if (serverId > existing) {
            readCursors[k] = serverId;
        }
    }

    function applyTyping(fromUserId: number, toUserId: number | null, roomId: number | null): void {
        // The server already routes typing events to peers/room members, but a
        // user with two tabs (web + another web) could echo their own typing
        // through both connections. Drop our own id defensively at store level
        // so it never bubbles up to the visible indicator.
        if (currentUserId.value !== null && fromUserId === currentUserId.value) {
            return;
        }
        const key: ThreadKey = roomId !== null
            ? { kind: "room", roomId }
            : { kind: "dm", peerUserId: fromUserId };
        const id = threadKeyId(key);
        const set = typingByThread[id] ?? (typingByThread[id] = new Set());
        set.add(fromUserId);
        // Auto-expire typing indicator after 3.5s of silence.
        window.setTimeout(() => { set.delete(fromUserId); }, 3500);
        void toUserId;
    }

    function directPeer(m: ChatMessage): number {
        const me = currentUserId.value;
        if (me !== null && m.fromUserId === me && m.toUserId !== null && m.toUserId !== undefined) {
            return m.toUserId;
        }
        return m.fromUserId;
    }

    function sortAsc(a: ChatMessage, b: ChatMessage): number {
        const aId = a.serverId ?? Number.MAX_SAFE_INTEGER;
        const bId = b.serverId ?? Number.MAX_SAFE_INTEGER;
        return aId - bId;
    }

    return {
        byThread,
        loadingThreads,
        typingByThread,
        readCursors,
        setCurrentUser,
        get,
        loadHistory,
        sendText,
        applyIncoming,
        applyMessageRead,
        applyTyping,
    };
});
