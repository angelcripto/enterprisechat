import { defineStore } from "pinia";
import { ref } from "vue";
import { api } from "@/api/client";
import type { ServerMetrics } from "@/api/types";

export const useMetricsStore = defineStore("metrics", () => {
    const info = ref<ServerMetrics | null>(null);

    async function load(): Promise<void> {
        const { data } = await api.get<ServerMetrics>("/metrics");
        info.value = data;
    }

    return { info, load };
});
