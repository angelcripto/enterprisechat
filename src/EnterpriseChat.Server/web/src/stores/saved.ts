import { defineStore } from "pinia";
import { ref } from "vue";
import { api } from "@/api/client";
import type { ChatMessage } from "@/api/types";

export interface SavedMessage extends ChatMessage {
    savedAt: string;
}

export const useSavedStore = defineStore("saved", () => {
    const items = ref<SavedMessage[]>([]);
    const loading = ref(false);

    async function load(): Promise<void> {
        loading.value = true;
        try {
            const { data } = await api.get<SavedMessage[]>("/me/saved");
            items.value = data;
        } finally {
            loading.value = false;
        }
    }

    async function toggle(messageId: number): Promise<boolean> {
        const { data } = await api.post<{ saved: boolean }>(`/messages/${messageId}/save`);
        await load();
        return data.saved;
    }

    return { items, loading, load, toggle };
});
