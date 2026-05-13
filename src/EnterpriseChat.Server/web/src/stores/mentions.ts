import { defineStore } from "pinia";
import { ref } from "vue";
import { api } from "@/api/client";
import type { ChatMessage } from "@/api/types";

export const useMentionsStore = defineStore("mentions", () => {
    const items = ref<ChatMessage[]>([]);
    const loading = ref(false);

    async function load(): Promise<void> {
        loading.value = true;
        try {
            const { data } = await api.get<ChatMessage[]>("/me/mentions");
            items.value = data;
        } finally {
            loading.value = false;
        }
    }

    return { items, loading, load };
});
