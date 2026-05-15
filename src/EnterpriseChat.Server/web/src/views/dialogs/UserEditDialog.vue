<script setup lang="ts">
import { onMounted, reactive, ref } from "vue";
import { Save } from "lucide-vue-next";
import Modal from "@/components/Modal.vue";
import { api } from "@/api/client";
import { useDialogsStore } from "@/stores/dialogs";

interface AdminUserDetail {
    id: number;
    username: string;
    fullName: string;
    email: string | null;
    departmentId: number | null;
    departmentName: string | null;
    role: string;
    isActive: boolean;
}

interface DepartmentSummary { id: number; name: string }

const props = defineProps<{
    /** Si null → modo creación. Si objeto → modo edición. */
    initial: AdminUserDetail | null;
}>();

const emit = defineEmits<{
    (e: "close"): void;
    (e: "saved"): void;
}>();

const dialogs = useDialogsStore();
const isNew = !props.initial;

const form = reactive({
    username: props.initial?.username ?? "",
    password: "",
    fullName: props.initial?.fullName ?? "",
    email: props.initial?.email ?? "",
    departmentId: props.initial?.departmentId ?? null as number | null,
    role: props.initial?.role ?? "User",
    isActive: props.initial?.isActive ?? true,
});

const departments = ref<DepartmentSummary[]>([]);
const submitting = ref(false);
const errorMsg = ref<string | null>(null);

onMounted(async () => {
    try {
        const { data } = await api.get<DepartmentSummary[]>("/admin/departments");
        departments.value = data;
    } catch { /* ignore */ }
});

async function save(): Promise<void> {
    errorMsg.value = null;
    if (isNew && form.password.length < 4) {
        errorMsg.value = "La contraseña inicial debe tener al menos 4 caracteres.";
        return;
    }
    if (!form.fullName.trim()) {
        errorMsg.value = "El nombre completo es obligatorio.";
        return;
    }
    submitting.value = true;
    try {
        if (isNew) {
            await api.post("/admin/users", {
                username: form.username.trim(),
                password: form.password,
                fullName: form.fullName.trim(),
                email: form.email.trim() || null,
                departmentId: form.departmentId,
                role: form.role,
            });
        } else if (props.initial) {
            await api.put(`/admin/users/${props.initial.id}`, {
                fullName: form.fullName.trim(),
                email: form.email.trim() || null,
                departmentId: form.departmentId,
                role: form.role,
                isActive: form.isActive,
            });
        }
        emit("saved");
        emit("close");
    } catch (err: unknown) {
        const e = err as { response?: { data?: { error?: string } }, message?: string };
        const msg = e.response?.data?.error ?? e.message ?? String(err);
        await dialogs.error("No se pudo guardar", msg);
    } finally {
        submitting.value = false;
    }
}
</script>

<template>
    <Modal :title="isNew ? 'Nuevo usuario' : `Editar: ${initial?.username}`" width="520px" @close="emit('close')">
        <div class="flex flex-col gap-3">
            <label v-if="isNew" class="flex flex-col gap-1">
                <span class="text-sm font-medium text-slate-700">Username</span>
                <input v-model="form.username" type="text" class="input" autocomplete="off" />
            </label>
            <label v-if="isNew" class="flex flex-col gap-1">
                <span class="text-sm font-medium text-slate-700">Contraseña inicial</span>
                <input v-model="form.password" type="password" class="input" autocomplete="new-password" />
            </label>
            <label class="flex flex-col gap-1">
                <span class="text-sm font-medium text-slate-700">Nombre completo</span>
                <input v-model="form.fullName" type="text" class="input" />
            </label>
            <label class="flex flex-col gap-1">
                <span class="text-sm font-medium text-slate-700">Email</span>
                <input v-model="form.email" type="email" class="input" />
            </label>
            <label class="flex flex-col gap-1">
                <span class="text-sm font-medium text-slate-700">Departamento</span>
                <select v-model.number="form.departmentId" class="input">
                    <option :value="null">(sin departamento)</option>
                    <option v-for="d in departments" :key="d.id" :value="d.id">{{ d.name }}</option>
                </select>
            </label>
            <label class="flex flex-col gap-1">
                <span class="text-sm font-medium text-slate-700">Rol</span>
                <select v-model="form.role" class="input">
                    <option value="User">User</option>
                    <option value="Admin">Admin</option>
                </select>
            </label>
            <label v-if="!isNew" class="flex items-center gap-2">
                <input type="checkbox" v-model="form.isActive" />
                <span class="text-sm">Activo</span>
            </label>
            <p v-if="errorMsg" class="text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg px-3 py-2">{{ errorMsg }}</p>
        </div>

        <template #footer>
            <button type="button" class="btn btn-secondary" @click="emit('close')">Cancelar</button>
            <button type="button" class="btn btn-primary" :disabled="submitting" @click="save">
                <Save class="w-4 h-4" />
                {{ submitting ? "Guardando…" : "Guardar" }}
            </button>
        </template>
    </Modal>
</template>
