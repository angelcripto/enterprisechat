<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { KeyRound, Plus, RotateCw, Ban, RefreshCw } from "lucide-vue-next";
import { useAuthStore } from "@/stores/auth";
import { useApiKeysStore, type ApiKeySummary, type IssuedApiKey } from "@/stores/apiKeys";
import { useDialogsStore } from "@/stores/dialogs";
import CreateApiKeyDialog from "@/views/dialogs/CreateApiKeyDialog.vue";
import IssuedApiKeySecretDialog from "@/views/dialogs/IssuedApiKeySecretDialog.vue";

const auth = useAuthStore();
const router = useRouter();
const store = useApiKeysStore();
const dialogs = useDialogsStore();

const createOpen = ref(false);
const issuedDialog = ref<{ data: IssuedApiKey; title: string } | null>(null);

const activeCount = computed(() => store.rows.filter((r) => !r.revokedAt).length);

onMounted(async () => {
    if (!auth.isAdmin) {
        await router.replace({ name: "inbox" });
        return;
    }
    await store.reload();
});

async function toggleIncludeRevoked(): Promise<void> {
    store.setIncludeRevoked(!store.includeRevoked);
    await store.reload();
}

function onIssued(issued: IssuedApiKey): void {
    issuedDialog.value = { data: issued, title: "Nueva clave creada" };
}

async function rotate(row: ApiKeySummary): Promise<void> {
    const ok = await dialogs.confirm({
        title: `¿Rotar la clave "${row.displayName}"?`,
        text: "Se generará un secreto nuevo y la clave actual quedará revocada al instante. Asegúrate de actualizar la integración antes de revocar permanentemente.",
        icon: "warning",
        confirmText: "Rotar",
        cancelText: "Cancelar",
    });
    if (!ok) return;
    try {
        const issued = await store.rotate(row.id, 0);
        issuedDialog.value = { data: issued, title: "Clave rotada" };
    } catch (err: unknown) {
        const e = err as { response?: { data?: { error?: string } }; message?: string };
        await dialogs.error("No se pudo rotar la clave", e.response?.data?.error ?? e.message ?? String(err));
    }
}

async function revoke(row: ApiKeySummary): Promise<void> {
    const reason = await dialogs.prompt({
        title: `Revocar "${row.displayName}"`,
        label: "Motivo (opcional, queda en el audit log)",
        inputType: "text",
        placeholder: "p.ej. integración retirada, token filtrado…",
    });
    if (reason === null) return;
    try {
        await store.revoke(row.id, reason.trim() === "" ? null : reason.trim());
        await dialogs.success("Clave revocada");
    } catch (err: unknown) {
        const e = err as { response?: { data?: { error?: string } }; message?: string };
        await dialogs.error("No se pudo revocar la clave", e.response?.data?.error ?? e.message ?? String(err));
    }
}

function fmtDate(iso: string | null): string {
    if (!iso) return "—";
    try { return new Date(iso).toLocaleString("es-ES"); }
    catch { return iso; }
}

function fmtShort(iso: string | null): string {
    if (!iso) return "—";
    try {
        const d = new Date(iso);
        return `${String(d.getDate()).padStart(2, "0")}/${String(d.getMonth() + 1).padStart(2, "0")} ${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`;
    } catch { return iso; }
}

function statusOf(row: ApiKeySummary): { label: string; cls: string } {
    if (row.revokedAt) return { label: "Revocada", cls: "bg-slate-100 text-slate-600" };
    if (row.expiresAt && new Date(row.expiresAt) < new Date()) return { label: "Caducada", cls: "bg-amber-100 text-amber-800" };
    return { label: "Activa", cls: "bg-emerald-100 text-emerald-800" };
}
</script>

<template>
    <div class="h-full overflow-y-auto bg-slate-50">
        <header class="sticky top-0 bg-white border-b border-slate-200 px-4 py-3 flex items-center gap-3 z-10">
            <KeyRound class="w-5 h-5 text-blue-600 flex-shrink-0" />
            <div class="min-w-0 flex-1">
                <h1 class="text-base font-semibold text-slate-900">API keys</h1>
                <p class="hidden sm:block text-xs text-slate-500">
                    Personal Access Tokens para integraciones. {{ activeCount }} activas.
                </p>
            </div>
            <button type="button" class="btn btn-secondary text-sm" @click="store.reload()" title="Refrescar">
                <RefreshCw class="w-4 h-4" />
                <span class="hidden sm:inline">Refrescar</span>
            </button>
            <button type="button" class="btn btn-primary flex-shrink-0" @click="createOpen = true">
                <Plus class="w-4 h-4" />
                <span class="hidden sm:inline">Nueva clave</span>
            </button>
        </header>

        <div class="px-4 py-4">
            <div class="flex items-center justify-between mb-3 text-sm">
                <label class="inline-flex items-center gap-2 cursor-pointer select-none">
                    <input
                        type="checkbox"
                        :checked="store.includeRevoked"
                        class="rounded border-slate-300"
                        @change="toggleIncludeRevoked"
                    />
                    <span class="text-slate-700">Mostrar revocadas</span>
                </label>
            </div>

            <div v-if="store.loading && store.rows.length === 0" class="text-sm text-slate-500 py-8 text-center">
                Cargando…
            </div>

            <div v-else-if="store.rows.length === 0" class="bg-white border border-slate-200 rounded-xl py-12 text-center">
                <KeyRound class="w-10 h-10 mx-auto text-slate-300 mb-3" />
                <p class="text-sm text-slate-700 font-medium">Aún no hay claves emitidas.</p>
                <p class="text-xs text-slate-500 mt-1">Crea la primera para empezar a integrar con la API.</p>
            </div>

            <div v-else class="bg-white border border-slate-200 rounded-xl overflow-hidden">
                <table class="w-full text-sm">
                    <thead class="bg-slate-50 text-xs uppercase tracking-wider text-slate-500">
                        <tr>
                            <th class="text-left px-3 py-2 font-semibold">Nombre</th>
                            <th class="text-left px-3 py-2 font-semibold">Rol</th>
                            <th class="text-left px-3 py-2 font-semibold hidden md:table-cell">Prefijo</th>
                            <th class="text-left px-3 py-2 font-semibold hidden lg:table-cell">Creada</th>
                            <th class="text-left px-3 py-2 font-semibold hidden lg:table-cell">Último uso</th>
                            <th class="text-left px-3 py-2 font-semibold hidden xl:table-cell">Caduca</th>
                            <th class="text-left px-3 py-2 font-semibold">Estado</th>
                            <th class="text-right px-3 py-2 font-semibold">Acciones</th>
                        </tr>
                    </thead>
                    <tbody class="divide-y divide-slate-100">
                        <tr v-for="row in store.rows" :key="row.id" :class="row.revokedAt ? 'opacity-60' : ''">
                            <td class="px-3 py-2">
                                <div class="font-medium text-slate-900 truncate max-w-[200px]" :title="row.displayName">{{ row.displayName }}</div>
                                <div v-if="row.notes" class="text-xs text-slate-500 truncate max-w-[200px]" :title="row.notes">{{ row.notes }}</div>
                            </td>
                            <td class="px-3 py-2">
                                <span :class="['px-2 py-0.5 rounded-full text-xs font-bold',
                                               row.role === 'Admin' ? 'bg-blue-100 text-blue-800' : 'bg-slate-100 text-slate-600']">
                                    {{ row.role }}
                                </span>
                            </td>
                            <td class="px-3 py-2 hidden md:table-cell font-mono text-xs text-slate-700">{{ row.prefix }}</td>
                            <td class="px-3 py-2 hidden lg:table-cell text-xs text-slate-500 whitespace-nowrap">{{ fmtShort(row.createdAt) }}</td>
                            <td class="px-3 py-2 hidden lg:table-cell text-xs text-slate-500 whitespace-nowrap">{{ fmtShort(row.lastUsedAt) }}</td>
                            <td class="px-3 py-2 hidden xl:table-cell text-xs text-slate-500 whitespace-nowrap">{{ fmtDate(row.expiresAt) }}</td>
                            <td class="px-3 py-2">
                                <span :class="['inline-flex items-center px-2 py-0.5 rounded-full text-xs font-bold', statusOf(row).cls]">
                                    {{ statusOf(row).label }}
                                </span>
                            </td>
                            <td class="px-3 py-2">
                                <div class="flex items-center justify-end gap-1 whitespace-nowrap">
                                    <button
                                        v-if="!row.revokedAt"
                                        type="button"
                                        class="icon-btn"
                                        :title="'Rotar — emite secreto nuevo y revoca este'"
                                        @click="rotate(row)"
                                    >
                                        <RotateCw class="w-4 h-4" />
                                    </button>
                                    <button
                                        v-if="!row.revokedAt"
                                        type="button"
                                        class="icon-btn icon-btn-danger"
                                        title="Revocar"
                                        @click="revoke(row)"
                                    >
                                        <Ban class="w-4 h-4" />
                                    </button>
                                </div>
                            </td>
                        </tr>
                    </tbody>
                </table>
            </div>
        </div>

        <CreateApiKeyDialog
            v-if="createOpen"
            @close="createOpen = false"
            @issued="onIssued"
        />

        <IssuedApiKeySecretDialog
            v-if="issuedDialog"
            :issued="issuedDialog.data"
            :title="issuedDialog.title"
            @close="issuedDialog = null"
        />
    </div>
</template>
