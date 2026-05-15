<script setup lang="ts">
import { useAuthStore } from "@/stores/auth";
import { useLicenseStore } from "@/stores/license";
import { isProEdition } from "@/api/types";

/**
 * Cabecera del sidebar: solo muestra el usuario y plan. Las acciones de
 * administración viven en la sección "Administración" del sidebar. El
 * componente sigue declarando el evento `invite` para compatibilidad,
 * aunque ya no lo emite — Sidebar.vue lo dispara directamente.
 */
const auth = useAuthStore();
const license = useLicenseStore();

defineEmits<{ (e: "invite"): void }>();
</script>

<template>
    <div class="w-full flex items-center gap-2 px-2 py-2">
        <span class="w-9 h-9 rounded-lg bg-blue-600 grid place-items-center text-white font-bold text-sm flex-shrink-0">EC</span>
        <span class="flex-1 min-w-0">
            <strong class="block text-slate-900 truncate">{{ auth.fullName || 'EnterpriseChat' }}</strong>
            <span v-if="isProEdition(license.info)" class="text-[10px] font-bold uppercase tracking-wider text-blue-600">Plan Pro</span>
            <span v-else class="text-[10px] font-medium uppercase tracking-wider text-slate-400">Plan Free</span>
        </span>
    </div>
</template>
