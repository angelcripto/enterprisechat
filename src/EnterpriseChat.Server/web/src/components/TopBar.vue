<script setup lang="ts">
import { ref } from "vue";
import { useRouter } from "vue-router";
import { useAuthStore } from "@/stores/auth";
import { useLicenseStore } from "@/stores/license";
import { chatHub } from "@/services/signalr";

const auth = useAuthStore();
const license = useLicenseStore();
const router = useRouter();
const query = ref("");

async function logout(): Promise<void> {
    await chatHub.stop();
    auth.clear();
    await router.replace({ name: "login" });
}

function initials(name: string): string {
    return name.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]).join("").toUpperCase();
}
</script>

<template>
    <header class="flex items-center justify-between px-4 bg-white">
        <div class="flex items-center gap-3">
            <span class="w-8 h-8 rounded-lg bg-blue-600 grid place-items-center text-white font-bold text-xs">EC</span>
            <strong class="text-slate-900">EnterpriseChat</strong>
            <span v-if="license.info?.edition === 'Pro'" class="px-2 py-0.5 rounded-full bg-blue-100 text-blue-800 text-xs font-bold">PRO</span>
            <span v-else-if="license.info?.edition === 'Free'" class="px-2 py-0.5 rounded-full bg-slate-100 text-slate-600 text-xs font-medium">Free</span>
        </div>

        <div class="flex-1 max-w-xl mx-6">
            <div class="relative">
                <input
                    v-model="query"
                    type="search"
                    placeholder="Buscar en EnterpriseChat (⌘K)"
                    class="input pl-9"
                    aria-label="Buscar"
                />
                <span class="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400">⌕</span>
            </div>
        </div>

        <div class="flex items-center gap-3">
            <div class="flex items-center gap-2">
                <span class="w-8 h-8 rounded-full bg-slate-200 grid place-items-center text-slate-700 text-xs font-bold">{{ initials(auth.fullName) }}</span>
                <div class="flex flex-col leading-tight">
                    <span class="text-sm font-medium text-slate-900">{{ auth.fullName }}</span>
                    <span class="text-xs text-slate-500">{{ auth.username }}</span>
                </div>
            </div>
            <button type="button" class="btn btn-ghost text-sm" @click="logout">Salir</button>
        </div>
    </header>
</template>
