<script setup lang="ts">
import { ref } from "vue";
import Modal from "@/components/Modal.vue";
import { api } from "@/api/client";
import { dialogError, dialogSuccess } from "@/dialogs";

const emit = defineEmits<{ (e: "close"): void; (e: "invited"): void }>();

const username = ref("");
const fullName = ref("");
const email = ref("");
const password = ref("");
const role = ref<"User" | "Admin">("User");
const submitting = ref(false);

async function submit(): Promise<void> {
    if (submitting.value) return;
    submitting.value = true;
    try {
        await api.post("/admin/users", {
            username: username.value.trim(),
            fullName: fullName.value.trim(),
            email: email.value.trim() || null,
            password: password.value,
            role: role.value,
            departmentId: null,
        });
        await dialogSuccess(`Usuario ${username.value} creado.`);
        emit("invited");
        emit("close");
    } catch (err: unknown) {
        const e = err as { response?: { data?: { error?: string } }, message?: string };
        await dialogError("No se pudo crear el usuario", e.response?.data?.error ?? e.message ?? "Error desconocido");
    } finally {
        submitting.value = false;
    }
}
</script>

<template>
    <Modal title="Invitar persona" @close="$emit('close')" width="480px">
        <form class="flex flex-col gap-4" @submit.prevent="submit">
            <div class="grid grid-cols-2 gap-3">
                <label class="flex flex-col gap-1">
                    <span class="text-sm font-medium text-slate-700">Usuario</span>
                    <input v-model="username" type="text" class="input" required autofocus />
                </label>
                <label class="flex flex-col gap-1">
                    <span class="text-sm font-medium text-slate-700">Rol</span>
                    <select v-model="role" class="input">
                        <option value="User">Usuario</option>
                        <option value="Admin">Administrador</option>
                    </select>
                </label>
            </div>
            <label class="flex flex-col gap-1">
                <span class="text-sm font-medium text-slate-700">Nombre completo</span>
                <input v-model="fullName" type="text" class="input" required />
            </label>
            <label class="flex flex-col gap-1">
                <span class="text-sm font-medium text-slate-700">Email (opcional)</span>
                <input v-model="email" type="email" class="input" />
            </label>
            <label class="flex flex-col gap-1">
                <span class="text-sm font-medium text-slate-700">Contraseña inicial</span>
                <input v-model="password" type="text" class="input font-mono" minlength="6" required />
                <small class="text-xs text-slate-500">Compártela con la persona; podrá cambiarla en su primer acceso.</small>
            </label>
        </form>

        <template #footer>
            <button type="button" class="btn btn-secondary" @click="$emit('close')">Cancelar</button>
            <button type="submit" class="btn btn-primary" :disabled="submitting" @click="submit">
                {{ submitting ? "Creando…" : "Crear usuario" }}
            </button>
        </template>
    </Modal>
</template>
