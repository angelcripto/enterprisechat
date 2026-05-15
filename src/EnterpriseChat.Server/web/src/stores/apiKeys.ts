import { defineStore } from "pinia";
import { ref } from "vue";
import { api } from "@/api/client";

/**
 * DTOs en sync con EnterpriseChat.Protocol.ApiKeys.ApiKeyDtos.
 * Los nombres camelCase los emite System.Text.Json con la política Web por
 * defecto que usan los minimal APIs.
 */
export interface ApiKeySummary {
    id: number;
    displayName: string;
    prefix: string;
    role: "User" | "Admin";
    createdAt: string;
    expiresAt: string | null;
    lastUsedAt: string | null;
    lastUsedIp: string | null;
    revokedAt: string | null;
    revokeReason: string | null;
    notes: string | null;
    rotatedFromId: number | null;
    createdByUserId: number | null;
}

export interface IssuedApiKey {
    plaintext: string;
    key: ApiKeySummary;
}

export interface CreateApiKeyPayload {
    displayName: string;
    role: "User" | "Admin";
    expiresInDays: number | null;
    notes: string | null;
}

export const useApiKeysStore = defineStore("apiKeys", () => {
    const rows = ref<ApiKeySummary[]>([]);
    const loading = ref(false);
    const includeRevoked = ref(false);

    async function reload(): Promise<void> {
        loading.value = true;
        try {
            const { data } = await api.get<{ rows: ApiKeySummary[] }>("/admin/api-keys", {
                params: { includeRevoked: includeRevoked.value ? true : null },
            });
            rows.value = data.rows;
        } finally {
            loading.value = false;
        }
    }

    async function create(payload: CreateApiKeyPayload): Promise<IssuedApiKey> {
        const { data } = await api.post<IssuedApiKey>("/admin/api-keys", payload);
        await reload();
        return data;
    }

    async function rotate(id: number, graceSeconds = 0): Promise<IssuedApiKey> {
        const { data } = await api.post<IssuedApiKey>(`/admin/api-keys/${id}/rotate`, { graceSeconds });
        await reload();
        return data;
    }

    async function revoke(id: number, reason: string | null): Promise<void> {
        await api.post(`/admin/api-keys/${id}/revoke`, { reason });
        await reload();
    }

    function setIncludeRevoked(v: boolean): void {
        includeRevoked.value = v;
    }

    return { rows, loading, includeRevoked, reload, create, rotate, revoke, setIncludeRevoked };
});
