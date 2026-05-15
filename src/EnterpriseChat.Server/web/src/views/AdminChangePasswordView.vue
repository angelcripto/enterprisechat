<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { ArrowLeft, KeyRound, Save } from "lucide-vue-next";
import { api } from "@/api/client";
import { useAuthStore } from "@/stores/auth";
import { dialogError, dialogSuccess } from "@/dialogs";

const auth = useAuthStore();
const router = useRouter();
const password = ref("");
const password2 = ref("");
const errorMsg = ref<string | null>(null);
const submitting = ref(false);

onMounted(async () => {
    if (!auth.isAdmin) {
        await router.replace({ name: "inbox" });
    }
});

async function submit(): Promise<void> {
    errorMsg.value = null;
    if (password.value.length < 8) {
        errorMsg.value = "La contraseña tiene que tener al menos 8 caracteres.";
        return;
    }
    if (password.value !== password2.value) {
        errorMsg.value = "Las contraseñas no coinciden.";
        return;
    }
    submitting.value = true;
    try {
        await api.post(`/admin/users/${auth.userId}/reset-password`, { newPassword: password.value });
        password.value = "";
        password2.value = "";
        await dialogSuccess("Contraseña cambiada", "La próxima vez que inicies sesión usa la nueva contraseña.");
    } catch (err: unknown) {
        const e = err as { response?: { data?: { error?: string } }, message?: string };
        await dialogError("No se pudo cambiar la contraseña", e.response?.data?.error ?? e.message ?? String(err));
    } finally {
        submitting.value = false;
    }
}
</script>

<template>
    <div class="bg-slate-50 px-6 py-8">
        <div class="max-w-xl mx-auto">
            <button type="button" @click="router.back()" class="inline-flex items-center gap-1.5 text-sm text-slate-600 hover:text-slate-900 mb-6">
                <ArrowLeft class="w-4 h-4" />
                Volver
            </button>

            <header class="mb-6">
                <h1 class="text-2xl font-bold text-slate-900 flex items-center gap-2">
                    <KeyRound class="w-6 h-6 text-blue-600" />
                    Cambiar clave admin
                </h1>
                <p class="text-sm text-slate-600 mt-1">
                    Actualiza la contraseña del usuario <code class="px-1 bg-slate-100 rounded">{{ auth.username }}</code>. Tu sesión actual sigue activa hasta que cierres.
                </p>
            </header>

            <form @submit.prevent="submit" class="card p-6 bg-white flex flex-col gap-3">
                <label class="flex flex-col gap-1">
                    <span class="text-sm font-medium text-slate-700">Nueva contraseña</span>
                    <input v-model="password" type="password" class="input" autocomplete="new-password" minlength="8" />
                </label>
                <label class="flex flex-col gap-1">
                    <span class="text-sm font-medium text-slate-700">Repite la nueva contraseña</span>
                    <input v-model="password2" type="password" class="input" autocomplete="new-password" minlength="8" />
                </label>

                <p v-if="errorMsg" class="text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg px-3 py-2">{{ errorMsg }}</p>

                <button type="submit" class="btn btn-primary self-start" :disabled="submitting">
                    <Save class="w-4 h-4" />
                    {{ submitting ? "Guardando…" : "Cambiar contraseña" }}
                </button>
            </form>
        </div>
    </div>
</template>
