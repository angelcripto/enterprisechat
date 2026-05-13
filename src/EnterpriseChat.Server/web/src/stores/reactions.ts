import { defineStore } from "pinia";
import { reactive } from "vue";
import { api } from "@/api/client";
import type { ReactionSummary } from "@/api/types";

export const useReactionsStore = defineStore("reactions", () => {
    const byMessage = reactive<Record<number, ReactionSummary[]>>({});

    async function load(messageId: number): Promise<void> {
        const { data } = await api.get<ReactionSummary[]>(`/messages/${messageId}/reactions`);
        byMessage[messageId] = data;
    }

    async function toggle(messageId: number, emoji: string): Promise<void> {
        await api.post(`/messages/${messageId}/reactions`, { emoji });
        await load(messageId);
    }

    function get(messageId: number): ReactionSummary[] {
        return byMessage[messageId] ?? [];
    }

    return { byMessage, load, toggle, get };
});
