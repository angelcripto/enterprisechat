<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { ArrowLeft, Download, Users, AlertTriangle, CheckCircle2 } from "lucide-vue-next";
import { api } from "@/api/client";
import { useAuthStore } from "@/stores/auth";
import { useDialogsStore } from "@/stores/dialogs";
import DataTable from "@/components/DataTable.vue";
import { usePaginatedTable } from "@/composables/usePaginatedTable";

interface BrowseRow {
    externalId: string;
    username: string;
    fullName: string | null;
    email: string | null;
    alreadyImported: boolean;
}

interface BrowseResult {
    rows: BrowseRow[];
    total: number;
    page: number;
    pageSize: number;
    licenseSlotsAvailable: number;
    licenseMaxUsers: number;
    licenseActiveUsers: number;
}

const auth = useAuthStore();
const route = useRoute();
const router = useRouter();
const dialogs = useDialogsStore();

const providerId = computed(() => parseInt(route.params.id as string, 10));
const providerName = ref("");

const licenseSlotsAvailable = ref(0);
const licenseMaxUsers = ref(0);
const licenseActiveUsers = ref(0);

const importing = ref(false);

const table = usePaginatedTable<BrowseRow, string>({
    loader: async ({ page, pageSize, search, sort, dir }) => {
        const { data } = await api.post<BrowseResult>(
            `/admin/auth-providers/${providerId.value}/browse`,
            { search: search || null, page, pageSize, sort, dir });
        licenseSlotsAvailable.value = data.licenseSlotsAvailable;
        licenseMaxUsers.value = data.licenseMaxUsers;
        licenseActiveUsers.value = data.licenseActiveUsers;
        return { rows: data.rows, total: data.total };
    },
    allIdsLoader: async ({ search, sort, dir }) => {
        const { data } = await api.post<{ ids: string[] }>(
            `/admin/auth-providers/${providerId.value}/all-ids`,
            { search: search || null, sort, dir });
        return { ids: data.ids };
    },
    rowKey: (r) => r.externalId,
    defaultSort: "username",
});

async function loadProviderName(): Promise<void> {
    try {
        const { data: detail } = await api.get<{ displayName: string }>(
            `/admin/auth-providers/${providerId.value}`);
        providerName.value = detail.displayName;
    } catch { /* ignore */ }
}

onMounted(async () => {
    if (!auth.isAdmin) {
        await router.replace({ name: "inbox" });
        return;
    }
    await Promise.all([loadProviderName(), table.reload()]);
});

function escapeHtml(s: string): string {
    return s.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]!));
}

async function importSelected(): Promise<void> {
    const externalIds = [...table.selected];
    if (externalIds.length === 0) return;

    // Si excede los slots libres, recortamos al cap y avisamos.
    let effective = externalIds;
    if (externalIds.length > licenseSlotsAvailable.value) {
        const ok = await dialogs.confirm({
            title: `Solo caben ${licenseSlotsAvailable.value} de ${externalIds.length}`,
            text: `La licencia ${licenseMaxUsers.value > 10 ? "Pro" : "Free"} permite ${licenseMaxUsers.value} cuentas activas en total. Ahora hay ${licenseActiveUsers.value}, quedan ${licenseSlotsAvailable.value} libres.\n\n¿Importar los primeros ${licenseSlotsAvailable.value} y omitir el resto?`,
            icon: "warning",
            confirmText: `Importar ${licenseSlotsAvailable.value}`,
        });
        if (!ok) return;
        effective = externalIds.slice(0, licenseSlotsAvailable.value);
    }

    importing.value = true;
    try {
        const { data: result } = await api.post<{ created: number; skipped: number; skippedReasons: string[] }>(
            `/admin/auth-providers/${providerId.value}/import`,
            { externalIds: effective });

        const reasonsHtml = result.skippedReasons.length === 0
            ? ""
            : `<details style="margin-top:1rem; text-align:left">
                 <summary style="cursor:pointer; font-weight:600; color:#475569">Ver detalle de omitidos (${result.skipped})</summary>
                 <ul style="margin-top:.5rem; padding-left:1.25rem; font-size:.85rem; color:#475569; max-height:200px; overflow-y:auto">
                   ${result.skippedReasons.map((r) => `<li>${escapeHtml(r)}</li>`).join("")}
                 </ul>
               </details>`;

        await dialogs.alert({
            icon: result.created > 0 ? "success" : "warning",
            title: result.created > 0 ? "Importación completada" : "No se importó ningún usuario",
            html: `
                <div style="display:flex; gap:2rem; justify-content:center; margin:1rem 0">
                  <div style="text-align:center">
                    <div style="font-size:2rem; font-weight:700; color:#059669">${result.created}</div>
                    <div style="font-size:.75rem; text-transform:uppercase; letter-spacing:.05em; color:#64748b">Creados</div>
                  </div>
                  <div style="text-align:center">
                    <div style="font-size:2rem; font-weight:700; color:#64748b">${result.skipped}</div>
                    <div style="font-size:.75rem; text-transform:uppercase; letter-spacing:.05em; color:#64748b">Omitidos</div>
                  </div>
                </div>
                ${reasonsHtml}
            `,
        });

        table.clearSelection();
        await table.reload();
    } catch (err: unknown) {
        const e = err as { response?: { data?: { error?: string } }, message?: string };
        await dialogs.error("No se pudo importar", e.response?.data?.error ?? e.message ?? String(err));
    } finally {
        importing.value = false;
    }
}

const columns = [
    { key: "username",   label: "Username",        sortable: true },
    { key: "fullName",   label: "Nombre completo" },
    { key: "email",      label: "Email",           sortable: true },
    { key: "externalId", label: "External ID",     sortable: true },
    { key: "status",     label: "Estado",          width: "120px", align: "right" as const },
] as const;
</script>

<template>
    <div class="bg-slate-50 px-3 sm:px-4 py-3 sm:py-4">
        <div class="w-full">
            <button type="button" @click="router.back()" class="inline-flex items-center gap-1.5 text-sm text-slate-600 hover:text-slate-900 mb-3 sm:mb-4">
                <ArrowLeft class="w-4 h-4" />
                Volver
            </button>

            <header class="mb-4">
                <h1 class="text-xl sm:text-2xl font-bold text-slate-900 flex items-center gap-2">
                    <Users class="w-5 h-5 sm:w-6 sm:h-6 text-blue-600 flex-shrink-0" />
                    <span class="truncate">Importar usuarios desde {{ providerName || `proveedor #${providerId}` }}</span>
                </h1>
                <p class="text-xs sm:text-sm text-slate-600 mt-1">
                    Selecciona qué usuarios traer al chat. Los marcados con check ya están importados — no consumen un nuevo slot.
                </p>
            </header>

            <div class="rounded-lg border border-blue-200 bg-blue-50 p-3 mb-4 flex items-start gap-3 text-xs sm:text-sm text-blue-900">
                <AlertTriangle class="w-4 h-4 sm:w-5 sm:h-5 flex-shrink-0 mt-0.5" />
                <div>
                    <strong>Licencia:</strong>
                    {{ licenseActiveUsers }} / {{ licenseMaxUsers }} cuentas activas — quedan
                    <strong>{{ licenseSlotsAvailable }}</strong> libres.
                </div>
            </div>

            <DataTable
                :columns="[...columns]"
                :controller="table"
                :row-key="(r: BrowseRow) => r.externalId"
                selectable
                :allow-select-all-matching="true"
                :row-disabled="(r: BrowseRow) => r.alreadyImported"
                placeholder-search="Buscar por username o email…"
            >
                <template #bulk-actions="{ count }">
                    <button type="button" class="btn btn-primary text-xs" :disabled="importing" @click="importSelected">
                        <Download class="w-3.5 h-3.5" />
                        {{ importing ? "Importando…" : `Importar ${count}` }}
                    </button>
                </template>

                <template #cell-username="{ row }: { row: BrowseRow }">
                    <span class="font-medium text-slate-900">{{ row.username }}</span>
                </template>
                <template #cell-fullName="{ row }: { row: BrowseRow }">
                    {{ row.fullName ?? "—" }}
                </template>
                <template #cell-email="{ row }: { row: BrowseRow }">
                    {{ row.email ?? "—" }}
                </template>
                <template #cell-externalId="{ row }: { row: BrowseRow }">
                    <span class="font-mono text-xs text-slate-500">{{ row.externalId }}</span>
                </template>
                <template #cell-status="{ row }: { row: BrowseRow }">
                    <span v-if="row.alreadyImported" class="inline-flex items-center gap-1 text-emerald-700 text-xs font-bold">
                        <CheckCircle2 class="w-3.5 h-3.5" />
                        Importado
                    </span>
                    <span v-else class="text-xs text-slate-400">Pendiente</span>
                </template>

                <template #mobile-card="{ row }: { row: BrowseRow }">
                    <div class="flex items-center justify-between gap-2">
                        <span class="font-medium text-slate-900 truncate flex-1">{{ row.username }}</span>
                        <span v-if="row.alreadyImported" class="inline-flex items-center gap-1 text-emerald-700 text-xs font-bold flex-shrink-0">
                            <CheckCircle2 class="w-3.5 h-3.5" />
                            Importado
                        </span>
                        <span v-else class="text-xs text-slate-400 flex-shrink-0">Pendiente</span>
                    </div>
                    <div v-if="row.fullName" class="text-sm text-slate-700 truncate">{{ row.fullName }}</div>
                    <div v-if="row.email" class="text-xs text-slate-500 truncate">{{ row.email }}</div>
                    <div class="text-xs font-mono text-slate-400 truncate">ID: {{ row.externalId }}</div>
                </template>

                <template #empty>No se encontraron usuarios.</template>
            </DataTable>
        </div>
    </div>
</template>
