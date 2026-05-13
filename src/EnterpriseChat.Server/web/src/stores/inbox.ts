import { defineStore } from "pinia";
import { ref } from "vue";
import { api } from "@/api/client";

export interface InboxEntry {
    kind: "room" | "dm";
    roomId: number | null;
    peerUserId: number | null;
    title: string;
    lastBody: string;
    lastAt: string;
    unreadCount: number;
    isPrivate: boolean;
}

export const useInboxStore = defineStore("inbox", () => {
    const items = ref<InboxEntry[]>([]);
    const loading = ref(false);

    async function load(): Promise<void> {
        loading.value = true;
        try {
            const { data } = await api.get<InboxEntry[]>("/me/inbox");
            items.value = data;
        } finally {
            loading.value = false;
        }
    }

    return { items, loading, load };
});
