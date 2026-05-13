<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { KeyRound, Shield, ArrowLeft, Server, Trash2 } from "lucide-vue-next";
import { api } from "@/api/client";
import { useAuthStore } from "@/stores/auth";
import { useLicenseStore } from "@/stores/license";
import { isProEdition } from "@/api/types";
import type { ApplyLicenseResponse } from "@/api/types";
import { dialogConfirm, dialogError, dialogSuccess } from "@/dialogs";

/**
 * Activación de licencia desde el cliente web. La petición va a /admin/license
 * en el server local (.NET), que internamente contacta con el backend de
 * producción (enterprisechat.es/activate) — el server traduce serial → JWT
 * firmado con la private key que solo tiene el backend de prod, y lo guarda.
 *
 * Por eso da igual que estemos en dev o en prod: TODO se valida contra prod.
 */
const auth = useAuthStore();
const license = useLicenseStore();
const router = useRouter();

const serial = ref("");
const submitting = ref(false);
const errorMsg = ref<string | null>(null);

const formattedExpires = computed(() => {
    if (!license.info?.expiresAt) return "Sin expiración";
    return new Date(license.info.expiresAt).toLocaleDateString("es-ES");
});

onMounted(async () => {
    if (!auth.isAdmin) {
        await router.replace({ name: "home" });
        return;
    }
    try { await license.load(); } catch { /* ignore */ }
});

function normalizeSerial(value: string): string {
    return value.toUpperCase().replace(/[^A-Z0-9-]/g, "").slice(0, 24);
}

function onSerialInput(): void {
    serial.value = normalizeSerial(serial.value);
}

async function activate(): Promise<void> {
    if (submitting.value) return;
    const trimmed = serial.value.trim();
    if (trimmed.length < 19) {
        errorMsg.value = "Pega el serial completo (ej. ECP-XXXX-XXXX-XXXX-XXXX).";
        return;
    }
    submitting.value = true;
    errorMsg.value = null;
    try {
        const { data } = await api.post<ApplyLicenseResponse>("/admin/license", { serial: trimmed });
        if (data.success) {
            await license.load();
            serial.value = "";
            await dialogSuccess("Licencia activada", "Tu servidor ya está validado contra enterprisechat.es.");
        } else {
            errorMsg.value = data.errorMessage ?? "El servidor de licencias rechazó el serial.";
        }
    } catch (err: unknown) {
        const e = err as { response?: { data?: { errorMessage?: string } }, message?: string };
        errorMsg.value = e.response?.data?.errorMessage ?? e.message ?? "Error al contactar con el servidor.";
    } finally {
        submitting.value = false;
    }
}

async function removeLicense(): Promise<void> {
    const ok = await dialogConfirm({
        title: "¿Quitar la licencia activa?",
        text: "El servidor volverá a la edición Free (10 usuarios concurrentes). Puedes reactivarla en cualquier momento.",
        confirmText: "Quitar licencia",
        cancelText: "Cancelar",
        danger: true,
    });
    if (!ok) return;
    try {
        await api.delete("/admin/license");
        await license.load();
        await dialogSuccess("Licencia retirada");
    } catch (err) {
        await dialogError("No se pudo quitar la licencia", err instanceof Error ? err.message : String(err));
    }
}
</script>

<template>
    <div class="min-h-screen bg-slate-50 px-6 py-8">
        <div class="max-w-3xl mx-auto">
            <button type="button" @click="router.back()" class="inline-flex items-center gap-1.5 text-sm text-slate-600 hover:text-slate-900 mb-6">
                <ArrowLeft class="w-4 h-4" />
                Volver al chat
            </button>

            <header class="mb-6">
                <h1 class="text-2xl font-bold text-slate-900 flex items-center gap-2">
                    <Shield class="w-6 h-6 text-blue-600" />
                    Licencia del servidor
                </h1>
                <p class="text-sm text-slate-600 mt-1">
                    Las licencias se validan siempre contra <strong>enterprisechat.es</strong>, da igual el entorno en el que corra este servidor.
                </p>
            </header>

            <section class="card p-6 mb-5 bg-white">
                <h2 class="text-sm font-bold uppercase tracking-wider text-slate-500 mb-4">Estado actual</h2>
                <div v-if="license.info" class="grid sm:grid-cols-2 gap-4 text-sm">
                    <div>
                        <div class="text-slate-500 text-xs uppercase tracking-wider mb-1">Edición</div>
                        <div class="flex items-center gap-2">
                            <span :class="['px-2.5 py-1 rounded-full text-xs font-bold uppercase tracking-wider',
                                isProEdition(license.info) ? 'bg-blue-100 text-blue-800' : 'bg-slate-100 text-slate-600']">
                                {{ isProEdition(license.info) ? 'Pro' : 'Free' }}
                            </span>
                        </div>
                    </div>
                    <div>
                        <div class="text-slate-500 text-xs uppercase tracking-wider mb-1">Máx. usuarios</div>
                        <div class="font-semibold text-slate-900">{{ license.info.maxConcurrentUsers }}</div>
                    </div>
                    <div v-if="license.info.licensedTo">
                        <div class="text-slate-500 text-xs uppercase tracking-wider mb-1">Licenciada a</div>
                        <div class="text-slate-900">{{ license.info.licensedTo }}</div>
                    </div>
                    <div v-if="license.info.expiresAt">
                        <div class="text-slate-500 text-xs uppercase tracking-wider mb-1">Caduca</div>
                        <div class="text-slate-900">{{ formattedExpires }}</div>
                    </div>
                </div>

                <div v-if="isProEdition(license.info)" class="mt-5 pt-4 border-t border-slate-100">
                    <button type="button" class="btn btn-secondary text-sm text-red-700 border-red-200 hover:bg-red-50" @click="removeLicense">
                        <Trash2 class="w-4 h-4" />
                        Quitar licencia activa
                    </button>
                </div>
            </section>

            <section class="card p-6 bg-white">
                <h2 class="text-sm font-bold uppercase tracking-wider text-slate-500 mb-2">
                    {{ isProEdition(license.info) ? 'Activar otra licencia' : 'Activar licencia Pro' }}
                </h2>
                <p class="text-xs text-slate-600 mb-4 flex items-start gap-1.5">
                    <Server class="w-3.5 h-3.5 mt-0.5 text-slate-400 flex-shrink-0" />
                    El serial se envía a enterprisechat.es. Tu servidor queda vinculado al hostname, IP pública y MAC actuales. Si lo mueves a otra máquina, contáctanos.
                </p>

                <form @submit.prevent="activate" class="flex flex-col gap-3">
                    <label class="flex flex-col gap-1">
                        <span class="text-sm font-medium text-slate-700">Serial</span>
                        <input
                            v-model="serial"
                            type="text"
                            class="input font-mono text-base tracking-wider"
                            placeholder="ECP-XXXX-XXXX-XXXX-XXXX"
                            maxlength="24"
                            spellcheck="false"
                            autocomplete="off"
                            @input="onSerialInput"
                        />
                    </label>

                    <p v-if="errorMsg" class="text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg px-3 py-2">{{ errorMsg }}</p>

                    <button type="submit" class="btn btn-primary self-start" :disabled="submitting">
                        <KeyRound class="w-4 h-4" />
                        {{ submitting ? 'Activando…' : 'Activar' }}
                    </button>
                </form>
            </section>
        </div>
    </div>
</template>
