<script setup lang="ts">
import { ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import axios from "axios";
import { useAuthStore } from "@/stores/auth";

const auth = useAuthStore();
const router = useRouter();
const route = useRoute();

const username = ref("");
const password = ref("");
const submitting = ref(false);
const error = ref<string | null>(null);

async function submit(): Promise<void> {
    if (submitting.value) return;
    error.value = null;
    submitting.value = true;
    try {
        await auth.login({ username: username.value.trim(), password: password.value });
        const redirect = typeof route.query.redirect === "string" ? route.query.redirect : "/";
        await router.replace(redirect);
    } catch (err) {
        if (axios.isAxiosError(err) && err.response?.status === 401) {
            error.value = "Usuario o contraseña incorrectos.";
        } else {
            error.value = "No se pudo conectar con el servidor.";
        }
    } finally {
        submitting.value = false;
    }
}
</script>

<template>
    <div class="min-h-screen grid place-items-center bg-gradient-to-br from-slate-50 to-blue-100 px-6">
        <div class="card w-full max-w-md p-8 shadow-xl">
            <div class="flex items-center gap-3 mb-6">
                <span class="w-10 h-10 rounded-xl bg-blue-600 grid place-items-center text-white font-bold">EC</span>
                <div>
                    <h1 class="text-lg font-bold text-slate-900">EnterpriseChat</h1>
                    <p class="text-xs text-slate-500">Comunicación interna autohospedada</p>
                </div>
            </div>

            <form @submit.prevent="submit" class="flex flex-col gap-4" autocomplete="on">
                <label class="flex flex-col gap-1">
                    <span class="text-sm font-medium text-slate-700">Usuario</span>
                    <input v-model="username" type="text" required autocomplete="username" class="input" placeholder="admin" />
                </label>

                <label class="flex flex-col gap-1">
                    <span class="text-sm font-medium text-slate-700">Contraseña</span>
                    <input v-model="password" type="password" required autocomplete="current-password" class="input" />
                </label>

                <p v-if="error" class="text-sm text-red-600 bg-red-50 border border-red-200 rounded-lg px-3 py-2">{{ error }}</p>

                <button type="submit" class="btn btn-primary mt-2" :disabled="submitting">
                    <span v-if="submitting">Entrando…</span>
                    <span v-else>Entrar</span>
                </button>
            </form>

            <p class="text-xs text-slate-400 text-center mt-6">
                ¿Olvidaste tu contraseña? Pide a tu administrador que la restablezca.
            </p>
        </div>
    </div>
</template>
