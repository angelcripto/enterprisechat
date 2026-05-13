import { defineStore } from "pinia";
import { computed, ref } from "vue";
import { api } from "@/api/client";
import type { UserSummary } from "@/api/types";

export const useUsersStore = defineStore("users", () => {
    const users = ref<UserSummary[]>([]);
    const loading = ref(false);

    const online = computed(() => users.value.filter((u) => u.isOnline));

    async function load(): Promise<void> {
        loading.value = true;
        try {
            const { data } = await api.get<UserSummary[]>("/users");
            users.value = data;
        } finally {
            loading.value = false;
        }
    }

    function findById(id: number): UserSummary | undefined {
        return users.value.find((u) => u.id === id);
    }

    function applyPresence(userId: number, isOnline: boolean): void {
        const u = users.value.find((x) => x.id === userId);
        if (u !== undefined) {
            u.isOnline = isOnline;
        }
    }

    return { users, loading, online, load, findById, applyPresence };
});
