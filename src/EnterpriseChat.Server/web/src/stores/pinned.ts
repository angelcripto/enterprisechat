import { defineStore } from "pinia";
import { reactive } from "vue";
import { api } from "@/api/client";
import type { PinnedSummary } from "@/api/types";

export const usePinnedStore = defineStore("pinned", () => {
    const byRoom = reactive<Record<number, PinnedSummary[]>>({});

    async function loadForRoom(roomId: number): Promise<void> {
        const { data } = await api.get<PinnedSummary[]>(`/rooms/${roomId}/pinned`);
        byRoom[roomId] = data;
    }

    async function pin(roomId: number, messageId: number): Promise<void> {
        await api.post(`/rooms/${roomId}/pinned/${messageId}`);
        await loadForRoom(roomId);
    }

    async function unpin(roomId: number, messageId: number): Promise<void> {
        await api.delete(`/rooms/${roomId}/pinned/${messageId}`);
        await loadForRoom(roomId);
    }

    function get(roomId: number): PinnedSummary[] {
        return byRoom[roomId] ?? [];
    }

    return { byRoom, loadForRoom, pin, unpin, get };
});
