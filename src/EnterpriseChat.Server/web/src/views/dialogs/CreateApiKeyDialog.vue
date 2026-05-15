<script setup lang="ts">
import { ref } from "vue";
import { Plus } from "lucide-vue-next";
import Modal from "@/components/Modal.vue";
import { useApiKeysStore, type IssuedApiKey } from "@/stores/apiKeys";

const emit = defineEmits<{
    (e: "close"): void;
    (e: "issued", v: IssuedApiKey): void;
}>();

const store = useApiKeysStore();

const displayName = ref("");
const role = ref<"User" | "Admin">("User");
const expiresInDays = ref<number | null>(null);
const notes = ref("");
const submitting = ref(false);
const errorMsg = ref<string | null>(null);

async function submit(): Promise<void> {
    if (submitting.value) return;
    const name = displayName.value.trim();
    if (!name) {
        errorMsg.value = "El nombre es obligatorio.";
        return;
    }
    if (name.length > 80) {
        errorMsg.value = "El nombre no puede pasar de 80 caracteres.";
        return;
    }
    if (expiresInDays.value !== null && expiresInDays.value < 0) {
        errorMsg.value = "Los días hasta caducar no pueden ser negativos.";
        return;
    }

    submitting.value = true;
    errorMsg.value = null;
    try {
        const issued = await store.create({
            displayName: name,
            role: role.value,
            expiresInDays: expiresInDays.value,
            notes: notes.value.trim() === "" ? null : notes.value.trim(),
        });
        emit("issued", issued);
        emit("close");
    } catch (err: unknown) {
        const e = err as { response?: { data?: { error?: string } }; message?: string };
        errorMsg.value = e.response?.data?.error ?? e.message ?? "No se pudo crear la clave.";
    } finally {
        submitting.value = false;
    }
}
</script>

<template>
    <Modal title="Nueva API key" width="480px" @close="emit('close')">
        <form class="flex flex-col gap-4" @submit.prevent="submit">
            <label class="flex flex-col gap-1">
                <span class="text-sm font-medium text-slate-700">Nombre</span>
                <input
                    v-model="displayName"
                    type="text"
                    class="input"
                    placeholder="Bot de turno, CI build…"
                    maxlength="80"
                    autocomplete="off"
                    autofocus
                />
                <span class="text-xs text-slate-500">Lo verás en la lista. Hasta 80 caracteres.</span>
            </label>

            <label class="flex flex-col gap-1">
                <span class="text-sm font-medium text-slate-700">Rol</span>
                <select v-model="role" class="input">
                    <option value="User">User (lectura general)</option>
                    <option value="Admin">Admin (incluye endpoints /admin/*)</option>
                </select>
                <span class="text-xs text-slate-500">
                    Las claves no representan a un usuario humano:
                    no pueden mandar DMs ni conectarse al hub de chat.
                </span>
            </label>

            <label class="flex flex-col gap-1">
                <span class="text-sm font-medium text-slate-700">Caduca en (días)</span>
                <input
                    v-model.number="expiresInDays"
                    type="number"
                    min="1"
                    class="input"
                    placeholder="Dejar vacío = no caduca"
                />
            </label>

            <label class="flex flex-col gap-1">
                <span class="text-sm font-medium text-slate-700">Notas (opcional)</span>
                <textarea
                    v-model="notes"
                    rows="2"
                    class="input"
                    maxlength="500"
                    placeholder="Owner, equipo responsable, etc."
                />
            </label>

            <p v-if="errorMsg" class="text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg px-3 py-2">
                {{ errorMsg }}
            </p>
        </form>

        <template #footer>
            <button type="button" class="btn btn-secondary" :disabled="submitting" @click="emit('close')">Cancelar</button>
            <button type="button" class="btn btn-primary" :disabled="submitting" @click="submit">
                <Plus class="w-4 h-4" />
                {{ submitting ? "Creando…" : "Crear clave" }}
            </button>
        </template>
    </Modal>
</template>
