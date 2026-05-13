import { defineStore } from "pinia";
import { ref } from "vue";
import { api } from "@/api/client";
import type { LicenseInfo } from "@/api/types";

export const useLicenseStore = defineStore("license", () => {
    const info = ref<LicenseInfo | null>(null);

    async function load(): Promise<void> {
        const { data } = await api.get<LicenseInfo>("/license");
        info.value = data;
    }

    return { info, load };
});
