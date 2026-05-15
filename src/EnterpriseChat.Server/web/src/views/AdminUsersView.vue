<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { ArrowLeft, Users, UserCheck, UserX, Pencil, KeyRound, Power, Trash2, Plus } from "lucide-vue-next";
import { api } from "@/api/client";
import { useAuthStore } from "@/stores/auth";
import { useDialogsStore } from "@/stores/dialogs";
import DataTable from "@/components/DataTable.vue";
import { usePaginatedTable } from "@/composables/usePaginatedTable";
import UserEditDialog from "@/views/dialogs/UserEditDialog.vue";

interface AdminUserDetail {
    id: number;
    username: string;
    fullName: string;
    email: string | null;
    departmentId: number | null;
    departmentName: string | null;
    role: string;
    isActive: boolean;
    createdAt: string;
    lastLoginAt: string | null;
}

interface AdminUserListResult {
    rows: AdminUserDetail[];
    total: number;
    page: number;
    pageSize: number;
}

const auth = useAuthStore();
const router = useRouter();
const dialogs = useDialogsStore();

const table = usePaginatedTable<AdminUserDetail, number>({
    loader: async ({ page, pageSize, search, sort, dir }) => {
        const { data } = await api.get<AdminUserListResult>("/admin/users", {
            params: { page, pageSize, search: search || null, sort, dir },
        });
        return { rows: data.rows, total: data.total };
    },
    allIdsLoader: async ({ search, sort, dir }) => {
        const { data } = await api.get<{ ids: number[] }>("/admin/users/ids", {
            params: { search: search || null, sort, dir },
        });
        return { ids: data.ids };
    },
    rowKey: (u) => u.id,
    defaultSort: "username",
});

const editingUser = ref<AdminUserDetail | null>(null);
const editorOpen = ref(false);

onMounted(async () => {
    if (!auth.isAdmin) {
        await router.replace({ name: "inbox" });
        return;
    }
    await table.reload();
});

function fmtDate(iso: string | null): string {
    if (!iso) return "—";
    try { return new Date(iso).toLocaleString("es-ES"); }
    catch { return iso; }
}

function fmtDateShort(iso: string | null): string {
    if (!iso) return "—";
    try {
        const d = new Date(iso);
        return `${String(d.getDate()).padStart(2, "0")}/${String(d.getMonth() + 1).padStart(2, "0")}/${d.getFullYear()} ${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`;
    } catch { return iso; }
}

function openNew(): void {
    editingUser.value = null;
    editorOpen.value = true;
}
function openEdit(u: AdminUserDetail): void {
    editingUser.value = u;
    editorOpen.value = true;
}
function closeEditor(): void {
    editorOpen.value = false;
}
async function onSaved(): Promise<void> {
    await table.reload();
}

async function resetPassword(u: AdminUserDetail): Promise<void> {
    const pwd = await dialogs.prompt({
        title: `Nueva contraseña para ${u.username}`,
        label: "Mínimo 4 caracteres",
        inputType: "password",
        validator: (v) => (v.length >= 4 ? null : "Demasiado corta."),
    });
    if (!pwd) return;
    try {
        await api.post(`/admin/users/${u.id}/reset-password`, { newPassword: pwd });
        await dialogs.success("Contraseña actualizada");
    } catch (err: unknown) {
        const e = err as { response?: { data?: { error?: string } }, message?: string };
        await dialogs.error("No se pudo cambiar la contraseña", e.response?.data?.error ?? e.message ?? String(err));
    }
}

async function toggleActive(u: AdminUserDetail): Promise<void> {
    try {
        if (u.isActive) {
            const ok = await dialogs.confirm({
                title: `¿Desactivar a ${u.fullName}?`,
                text: "No podrá iniciar sesión hasta que lo reactives.",
                icon: "warning",
                confirmText: "Desactivar",
                danger: true,
            });
            if (!ok) return;
            await api.delete(`/admin/users/${u.id}`);
        } else {
            await api.post(`/admin/users/${u.id}/activate`);
        }
        await table.reload();
    } catch (err: unknown) {
        const e = err as { response?: { data?: { error?: string } }, message?: string };
        await dialogs.error("No se pudo cambiar el estado", e.response?.data?.error ?? e.message ?? String(err));
    }
}

async function hardDelete(u: AdminUserDetail): Promise<void> {
    const ok = await dialogs.confirm({
        title: `Borrar definitivamente a "${u.fullName}"`,
        text: `Los mensajes, adjuntos y salas que haya creado se reasignarán al usuario sistema "Usuario eliminado". Las sesiones, reacciones y mensajes guardados se borran.\n\nEsta acción es IRREVERSIBLE.`,
        icon: "error",
        confirmText: "Sí, borrar definitivamente",
        cancelText: "Cancelar",
        danger: true,
    });
    if (!ok) return;
    try {
        await api.delete(`/admin/users/${u.id}/hard`);
        await dialogs.success("Usuario eliminado");
        await table.reload();
    } catch (err: unknown) {
        const e = err as { response?: { data?: { error?: string } }, message?: string };
        await dialogs.error("No se pudo eliminar", e.response?.data?.error ?? e.message ?? String(err));
    }
}

async function bulkDeactivate(): Promise<void> {
    const ids = [...table.selected];
    if (ids.length === 0) return;
    const ok = await dialogs.confirm({
        title: `¿Desactivar ${ids.length} usuarios?`,
        text: "No podrán iniciar sesión hasta que los reactives uno a uno.",
        icon: "warning",
        danger: true,
    });
    if (!ok) return;
    await runConcurrentLimited(ids.filter((id) => id !== auth.userId), 5, async (id) => {
        await api.delete(`/admin/users/${id}`);
    });
    table.clearSelection();
    await table.reload();
    await dialogs.success("Hecho");
}

async function bulkHardDelete(): Promise<void> {
    const ids = [...table.selected].filter((id) => id !== auth.userId);
    if (ids.length === 0) return;
    const ok = await dialogs.confirm({
        title: `¿Borrar definitivamente ${ids.length} usuarios?`,
        text: "Acción irreversible. Los mensajes que hubieran escrito se anonimizan.",
        icon: "warning",
        confirmText: "Borrar",
        danger: true,
    });
    if (!ok) return;
    await runConcurrentLimited(ids, 5, async (id) => {
        await api.delete(`/admin/users/${id}/hard`);
    });
    table.clearSelection();
    await table.reload();
    await dialogs.success("Hecho");
}

async function runConcurrentLimited<T>(items: T[], limit: number, worker: (item: T) => Promise<void>): Promise<void> {
    let cursor = 0;
    async function next(): Promise<void> {
        while (cursor < items.length) {
            const idx = cursor++;
            try { await worker(items[idx]!); } catch { /* swallow per-item; bulk muestra success agregado */ }
        }
    }
    const workers = Array.from({ length: Math.min(limit, items.length) }, () => next());
    await Promise.all(workers);
}

const columns = [
    { key: "username",    label: "Usuario",      sortable: true },
    { key: "fullName",    label: "Nombre",       sortable: true },
    { key: "email",       label: "Email",        sortable: true, hideUntil: "xl" as const },
    { key: "role",        label: "Rol",          sortable: true, width: "80px" },
    { key: "department",  label: "Depto",        hideUntil: "xl" as const },
    { key: "lastLoginAt", label: "Último",       sortable: true, width: "130px", hideUntil: "lg" as const },
    { key: "isActive",    label: "Estado",       sortable: true, width: "100px" },
] as const;
</script>

<template>
    <div class="h-full overflow-y-auto bg-slate-50">
        <header class="sticky top-0 bg-white border-b border-slate-200 px-4 py-3 flex items-center gap-3 z-10">
            <Users class="w-5 h-5 text-blue-600 flex-shrink-0" />
            <div class="min-w-0 flex-1">
                <h1 class="text-base font-semibold text-slate-900">Usuarios</h1>
                <p class="hidden sm:block text-xs text-slate-500">Crear, editar, activar/desactivar, cambiar contraseña y borrar.</p>
            </div>
            <button type="button" class="btn btn-primary flex-shrink-0" @click="openNew">
                <Plus class="w-4 h-4" />
                <span class="hidden sm:inline">Nuevo usuario</span>
            </button>
        </header>

            <DataTable
                :columns="[...columns]"
                :controller="table"
                :row-key="(u: AdminUserDetail) => u.id"
                selectable
                :allow-select-all-matching="true"
                placeholder-search="Buscar por usuario, nombre o email…"
            >
                <template #bulk-actions>
                    <button type="button" class="btn btn-secondary text-xs" @click="bulkDeactivate">
                        <Power class="w-3.5 h-3.5" />
                        Desactivar
                    </button>
                    <button type="button" class="btn btn-secondary text-xs text-red-700 border-red-200 hover:bg-red-50" @click="bulkHardDelete">
                        <Trash2 class="w-3.5 h-3.5" />
                        Borrar
                    </button>
                </template>

                <template #cell-username="{ row }: { row: AdminUserDetail }">
                    <span class="font-medium text-slate-900">{{ row.username }}</span>
                </template>
                <template #cell-email="{ row }: { row: AdminUserDetail }">
                    {{ row.email ?? "—" }}
                </template>
                <template #cell-role="{ row }: { row: AdminUserDetail }">
                    <span :class="['px-2 py-0.5 rounded-full text-xs font-bold', row.role === 'Admin' ? 'bg-blue-100 text-blue-800' : 'bg-slate-100 text-slate-600']">{{ row.role }}</span>
                </template>
                <template #cell-department="{ row }: { row: AdminUserDetail }">
                    {{ row.departmentName ?? "—" }}
                </template>
                <template #cell-lastLoginAt="{ row }: { row: AdminUserDetail }">
                    <span class="text-slate-500 text-xs whitespace-nowrap">{{ fmtDateShort(row.lastLoginAt) }}</span>
                </template>
                <template #cell-isActive="{ row }: { row: AdminUserDetail }">
                    <span :class="['inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-bold',
                        row.isActive ? 'bg-emerald-100 text-emerald-800' : 'bg-slate-100 text-slate-600']">
                        <component :is="row.isActive ? UserCheck : UserX" class="w-3 h-3" />
                        {{ row.isActive ? "Activo" : "Desactivado" }}
                    </span>
                </template>

                <template #row-actions="{ row }: { row: AdminUserDetail }">
                    <div class="flex items-center justify-end gap-1 whitespace-nowrap">
                        <button class="icon-btn" @click="openEdit(row)" title="Editar"><Pencil class="w-4 h-4" /></button>
                        <button class="icon-btn" @click="resetPassword(row)" title="Cambiar contraseña"><KeyRound class="w-4 h-4" /></button>
                        <button v-if="row.id !== auth.userId" class="icon-btn" @click="toggleActive(row)" :title="row.isActive ? 'Desactivar' : 'Activar'"><Power class="w-4 h-4" /></button>
                        <button v-if="row.id !== auth.userId" class="icon-btn icon-btn-danger" @click="hardDelete(row)" title="Borrar definitivamente"><Trash2 class="w-4 h-4" /></button>
                    </div>
                </template>

                <template #mobile-card="{ row }: { row: AdminUserDetail }">
                    <div class="flex items-center justify-between gap-2 flex-wrap">
                        <div class="font-medium text-slate-900 truncate flex-1 min-w-0">{{ row.username }}</div>
                        <span :class="['px-2 py-0.5 rounded-full text-xs font-bold flex-shrink-0', row.role === 'Admin' ? 'bg-blue-100 text-blue-800' : 'bg-slate-100 text-slate-600']">{{ row.role }}</span>
                    </div>
                    <div class="text-sm text-slate-700 truncate">{{ row.fullName }}</div>
                    <div v-if="row.email" class="text-xs text-slate-500 truncate">{{ row.email }}</div>
                    <div class="mt-1 flex items-center gap-2 text-xs text-slate-500 flex-wrap">
                        <span :class="['inline-flex items-center gap-1 px-2 py-0.5 rounded-full font-bold',
                            row.isActive ? 'bg-emerald-100 text-emerald-800' : 'bg-slate-100 text-slate-600']">
                            <component :is="row.isActive ? UserCheck : UserX" class="w-3 h-3" />
                            {{ row.isActive ? 'Activo' : 'Desactivado' }}
                        </span>
                        <span v-if="row.lastLoginAt">Último: {{ fmtDate(row.lastLoginAt) }}</span>
                        <span v-if="row.departmentName">· {{ row.departmentName }}</span>
                    </div>
                    <div class="mt-3 flex items-stretch gap-1.5">
                        <button class="btn-mobile-row" @click="openEdit(row)">
                            <Pencil class="w-3.5 h-3.5" /> Editar
                        </button>
                        <button class="btn-mobile-row" @click="resetPassword(row)">
                            <KeyRound class="w-3.5 h-3.5" /> Pass
                        </button>
                        <button v-if="row.id !== auth.userId" class="btn-mobile-row" @click="toggleActive(row)">
                            <Power class="w-3.5 h-3.5" />
                            {{ row.isActive ? 'Off' : 'On' }}
                        </button>
                        <button v-if="row.id !== auth.userId" class="btn-mobile-row btn-mobile-row-danger" @click="hardDelete(row)">
                            <Trash2 class="w-3.5 h-3.5" /> Borrar
                        </button>
                    </div>
                </template>

                <template #empty>No hay usuarios que coincidan con el filtro.</template>
            </DataTable>

        <UserEditDialog v-if="editorOpen" :initial="editingUser" @close="closeEditor" @saved="onSaved" />
    </div>
</template>
