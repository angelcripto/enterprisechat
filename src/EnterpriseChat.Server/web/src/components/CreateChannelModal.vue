<script setup lang="ts">
import { ref } from "vue";
import { Hash, Lock } from "lucide-vue-next";
import Modal from "@/components/Modal.vue";
import { useRoomsStore } from "@/stores/rooms";
import { dialogError } from "@/dialogs";

const emit = defineEmits<{ (e: "close"): void; (e: "created", roomId: number): void }>();
const rooms = useRoomsStore();

const name = ref("");
const isPrivate = ref(false);
const submitting = ref(false);
const nameError = ref<string | null>(null);

function normalize(value: string): string {
    return value.toLowerCase().replace(/\s+/g, "-").replace(/[^a-z0-9\-_áéíóúñ]/g, "");
}

function onNameInput(): void {
    name.value = normalize(name.value);
    nameError.value = null;
}

async function submit(): Promise<void> {
    if (submitting.value) return;
    const trimmed = name.value.trim();
    if (trimmed === "") {
        nameError.value = "Escribe un nombre.";
        return;
    }
    if (trimmed.length > 64) {
        nameError.value = "Máximo 64 caracteres.";
        return;
    }
    submitting.value = true;
    try {
        const id = await rooms.create(trimmed, isPrivate.value);
        emit("created", id);
        emit("close");
    } catch (err) {
        await dialogError("No se pudo crear el canal", err instanceof Error ? err.message : String(err));
    } finally {
        submitting.value = false;
    }
}
</script>

<template>
    <Modal title="Nuevo canal" @close="$emit('close')">
        <form class="flex flex-col gap-4" @submit.prevent="submit">
            <label class="flex flex-col gap-1.5">
                <span class="text-sm font-medium text-slate-700">Nombre del canal</span>
                <div class="relative">
                    <span class="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400">
                        <component :is="isPrivate ? Lock : Hash" class="w-4 h-4" />
                    </span>
                    <input
                        v-model="name"
                        type="text"
                        class="input pl-9"
                        placeholder="p. ej. ventas"
                        maxlength="64"
                        autofocus
                        @input="onNameInput"
                    />
                </div>
                <small v-if="nameError" class="text-xs text-red-600">{{ nameError }}</small>
                <small v-else class="text-xs text-slate-500">Letras minúsculas, números y guiones. Se ve precedido por # (o 🔒 si es privado).</small>
            </label>

            <fieldset class="flex flex-col gap-2 rounded-lg border border-slate-200 p-3">
                <legend class="text-xs font-bold uppercase tracking-wider text-slate-500 px-1">Visibilidad</legend>
                <label class="flex items-start gap-2.5">
                    <input type="radio" :checked="!isPrivate" @change="isPrivate = false" name="vis" />
                    <span>
                        <span class="block text-sm font-medium text-slate-900 flex items-center gap-1.5">
                            <Hash class="w-3.5 h-3.5 text-slate-500" />
                            Público
                        </span>
                        <span class="block text-xs text-slate-500">Cualquier persona del workspace puede ver y unirse.</span>
                    </span>
                </label>
                <label class="flex items-start gap-2.5">
                    <input type="radio" :checked="isPrivate" @change="isPrivate = true" name="vis" />
                    <span>
                        <span class="block text-sm font-medium text-slate-900 flex items-center gap-1.5">
                            <Lock class="w-3.5 h-3.5 text-slate-500" />
                            Privado
                        </span>
                        <span class="block text-xs text-slate-500">Solo visible para las personas que invites manualmente.</span>
                    </span>
                </label>
            </fieldset>
        </form>

        <template #footer>
            <button type="button" class="btn btn-secondary" @click="$emit('close')">Cancelar</button>
            <button type="submit" class="btn btn-primary" :disabled="submitting" @click="submit">
                {{ submitting ? "Creando…" : "Crear canal" }}
            </button>
        </template>
    </Modal>
</template>
