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

    /**
     * Quién está escribiendo, por hilo: `{ "dm:7": { 7: 1739284… } }`
     * (hilo → id de usuario → instante del último aviso).
     *
     * Es un objeto plano a propósito, no un `Set`. Un `Set` dentro de
     * `reactive()` tiene una trampa: al crearlo con
     * `x[id] ?? (x[id] = new Set())`, la expresión devuelve el Set CRUDO en vez
     * del proxy reactivo, y mutarlo después no repinta nada. Los objetos planos
     * no tienen ese problema, y de paso guardar la marca de tiempo permite
     * depurar cuándo llegó el último aviso.
     */
    const typingByThread = reactive<Record<string, Record<number, number>>>({});

    /** Temporizadores de expiración, fuera del estado reactivo: son detalle de
     *  implementación y no deben provocar repintados. Clave: `"{hilo}:{userId}"`. */
    const typingTimers: Record<string, number> = {};

    /** El emisor refresca su aviso cada 2s; 3,5s da margen a una pulsación
     *  perdida sin dejar el indicador colgado si el otro cierra la pestaña. */
    const TYPING_TTL_MS = 3500;

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

    /**
     * Aplica un aviso de "está escribiendo" recibido del hub.
     *
     * `isTyping: false` lo borra al instante (el emisor envió el mensaje o vació
     * el cuadro). Si no llega ese aviso, el temporizador lo retira solo.
     */
    function applyTyping(
        fromUserId: number,
        toUserId: number | null,
        roomId: number | null,
        isTyping = true,
    ): void {
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

        if (!isTyping) {
            clearTyping(id, fromUserId);
            void toUserId;
            return;
        }

        // OJO: hay que escribir SIEMPRE a través de `typingByThread[id]`, nunca
        // sobre una referencia local guardada antes. El valor de una asignación
        // en JavaScript es el objeto CRUDO asignado, no el proxy reactivo que
        // Vue devuelve al leer la clave. Guardar esa referencia y mutarla
        // después cambia el dato pero NO repinta la pantalla: ese era el bug
        // que dejaba el "está escribiendo…" clavado para siempre.
        if (typingByThread[id] === undefined) {
            typingByThread[id] = {};
        }
        typingByThread[id]![fromUserId] = Date.now();

        // Un temporizador por (hilo, usuario), reiniciado en cada aviso. Antes
        // cada pulsación programaba su propio setTimeout sin cancelar el
        // anterior: con el throttle de 2s del emisor, el timer del primer aviso
        // borraba el indicador a los 3,5s aunque siguieras escribiendo → parpadeo.
        const timerKey = `${id}:${fromUserId}`;
        window.clearTimeout(typingTimers[timerKey]);
        typingTimers[timerKey] = window.setTimeout(
            () => { clearTyping(id, fromUserId); },
            TYPING_TTL_MS,
        );
        void toUserId;
    }

    function clearTyping(threadId: string, userId: number): void {
        const timerKey = `${threadId}:${userId}`;
        window.clearTimeout(typingTimers[timerKey]);
        delete typingTimers[timerKey];

        const thread = typingByThread[threadId];
        if (thread === undefined) return;
        delete thread[userId];
        if (Object.keys(thread).length === 0) {
            delete typingByThread[threadId];
        }
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
