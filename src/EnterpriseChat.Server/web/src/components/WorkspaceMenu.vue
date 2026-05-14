<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { ChevronDown, Shield, ShieldCheck, UserPlus, LogOut } from "lucide-vue-next";
import { useAuthStore } from "@/stores/auth";
import { useLicenseStore } from "@/stores/license";
import { chatHub } from "@/services/signalr";
import { isProEdition } from "@/api/types";
import { dialogConfirm } from "@/dialogs";

const auth = useAuthStore();
const license = useLicenseStore();
const router = useRouter();

const open = ref(false);
const root = ref<HTMLElement | null>(null);

function toggle(): void { open.value = !open.value; }
function close(): void { open.value = false; }

function onDocClick(e: MouseEvent): void {
    if (root.value && !root.value.contains(e.target as Node)) close();
}

onMounted(() => { document.addEventListener("click", onDocClick); });
onBeforeUnmount(() => { document.removeEventListener("click", onDocClick); });

function goLicense(): void {
    close();
    void router.push({ name: "admin-license" });
}

function goAuthProviders(): void {
    close();
    void router.push({ name: "admin-auth-providers" });
}

const emit = defineEmits<{ (e: "invite"): void }>();
function invite(): void {
    close();
    emit("invite");
}

async function logout(): Promise<void> {
    close();
    const ok = await dialogConfirm({ title: "¿Cerrar sesión?", confirmText: "Cerrar sesión", icon: "question" });
    if (!ok) return;
    await chatHub.stop();
    auth.clear();
    await router.replace({ name: "login" });
}
</script>

<template>
    <div ref="root" class="relative">
        <button type="button" class="w-full flex items-center gap-2 px-2 py-2 rounded-lg hover:bg-slate-50 text-left" @click="toggle">
            <span class="w-9 h-9 rounded-lg bg-blue-600 grid place-items-center text-white font-bold text-sm flex-shrink-0">EC</span>
            <span class="flex-1 min-w-0">
                <strong class="block text-slate-900 truncate">{{ auth.fullName || 'EnterpriseChat' }}</strong>
                <span v-if="isProEdition(license.info)" class="text-[10px] font-bold uppercase tracking-wider text-blue-600">Plan Pro</span>
                <span v-else class="text-[10px] font-medium uppercase tracking-wider text-slate-400">Plan Free</span>
            </span>
            <ChevronDown class="w-4 h-4 text-slate-400 flex-shrink-0 transition-transform" :class="{ 'rotate-180': open }" />
        </button>

        <div v-if="open" class="absolute left-0 right-0 mt-2 bg-white border border-slate-200 rounded-xl shadow-lg overflow-hidden z-30">
            <div class="px-3 py-2 border-b border-slate-100">
                <div class="text-xs text-slate-500 uppercase tracking-wider font-semibold">Sesión</div>
                <div class="text-sm font-medium text-slate-900 truncate">{{ auth.fullName }}</div>
                <div class="text-xs text-slate-500 truncate">{{ auth.username }}</div>
            </div>
            <button v-if="auth.isAdmin" type="button" class="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-slate-700 hover:bg-slate-50" @click="goLicense">
                <Shield class="w-4 h-4 text-slate-500" />
                Gestionar licencia
            </button>
            <button v-if="auth.isAdmin" type="button" class="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-slate-700 hover:bg-slate-50" @click="goAuthProviders">
                <ShieldCheck class="w-4 h-4 text-slate-500" />
                Autenticación externa
            </button>
            <button v-if="auth.isAdmin" type="button" class="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-slate-700 hover:bg-slate-50" @click="invite">
                <UserPlus class="w-4 h-4 text-slate-500" />
                Invitar personas
            </button>
            <button type="button" class="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-red-600 hover:bg-red-50 border-t border-slate-100" @click="logout">
                <LogOut class="w-4 h-4" />
                Cerrar sesión
            </button>
        </div>
    </div>
</template>
