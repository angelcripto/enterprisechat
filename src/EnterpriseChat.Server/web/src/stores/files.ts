import { defineStore } from "pinia";
import { reactive } from "vue";
import { api } from "@/api/client";
import type { AttachmentSummary } from "@/api/types";

export const useFilesStore = defineStore("files", () => {
    const byRoom = reactive<Record<number, AttachmentSummary[]>>({});

    async function loadForRoom(roomId: number): Promise<void> {
        const { data } = await api.get<AttachmentSummary[]>(`/rooms/${roomId}/files`);
        byRoom[roomId] = data;
    }

    function get(roomId: number): AttachmentSummary[] {
        return byRoom[roomId] ?? [];
    }

    return { byRoom, loadForRoom, get };
});
