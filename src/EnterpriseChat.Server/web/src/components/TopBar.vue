<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount, ref } from "vue";
import { useRouter } from "vue-router";
import { Search, Filter, HelpCircle, ChevronDown, LogOut, Image as ImageIcon } from "lucide-vue-next";
import Avatar from "@/components/Avatar.vue";
import { useAuthStore } from "@/stores/auth";
import { useUsersStore } from "@/stores/users";
import { useLicenseStore } from "@/stores/license";
import { chatHub } from "@/services/signalr";
import { api } from "@/api/client";
import { isProEdition } from "@/api/types";
import { dialogConfirm, dialogError, dialogSuccess } from "@/dialogs";

const auth = useAuthStore();
const users = useUsersStore();
const license = useLicenseStore();
const router = useRouter();

const query = ref("");
const connected = ref(false);
const menuOpen = ref(false);
const avatarFileInput = ref<HTMLInputElement | null>(null);

const me = computed(() => users.users.find((u) => u.id === auth.userId));

function pollConnection(): void {
    connected.value = chatHub.isConnected();
}

let timer = 0;
onMounted(() => {
    pollConnection();
    timer = window.setInterval(pollConnection, 2000);
});
onBeforeUnmount(() => { if (timer !== 0) window.clearInterval(timer); });

async function logout(): Promise<void> {
    menuOpen.value = false;
    const ok = await dialogConfirm({ title: "¿Cerrar sesión?", confirmText: "Cerrar sesión", icon: "question" });
    if (!ok) return;
    await chatHub.stop();
    auth.clear();
    await router.replace({ name: "login" });
}

function openAvatarPicker(): void {
    menuOpen.value = false;
    avatarFileInput.value?.click();
}

async function onAvatarSelected(ev: Event): Promise<void> {
    const target = ev.target as HTMLInputElement;
    const file = target.files?.[0];
    target.value = "";
    if (file === undefined) return;
    try {
        const fd = new FormData();
        fd.append("file", file);
        await api.post("/users/me/avatar", fd);
        await users.load();
        await dialogSuccess("Avatar actualizado");
    } catch (err) {
        await dialogError("No se pudo subir el avatar", err instanceof Error ? err.message : String(err));
    }
}
</script>

<template>
    <header class="flex items-center justify-between px-4 bg-white gap-4">
        <div class="flex items-center gap-3 min-w-0">
            <img src="/logo.png" alt="EnterpriseChat" class="h-8 w-auto" />
            <span class="hidden md:inline-flex items-center gap-1.5 text-xs text-slate-500">
                <span class="w-2 h-2 rounded-full" :class="connected ? 'bg-emerald-500' : 'bg-slate-300'"></span>
                {{ connected ? 'Conectado' : 'Desconectado' }}
            </span>
            <span v-if="isProEdition(license.info)" class="px-2 py-0.5 rounded-full bg-blue-100 text-blue-800 text-[10px] font-bold uppercase tracking-wider">Pro</span>
        </div>

        <div class="flex-1 max-w-xl mx-2">
            <div class="relative">
                <input v-model="query" type="search" placeholder="Buscar en EnterpriseChat" class="input pl-9 pr-12" aria-label="Buscar" />
                <Search class="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
                <kbd class="absolute right-3 top-1/2 -translate-y-1/2 hidden sm:inline-block px-1.5 py-0.5 text-[10px] font-mono text-slate-400 bg-slate-100 rounded">⌘K</kbd>
            </div>
        </div>

        <div class="flex items-center gap-2">
            <button type="button" class="hidden md:inline-flex items-center gap-1.5 btn btn-secondary text-sm">
                <Filter class="w-4 h-4" />
                Filtrar
            </button>
            <button type="button" class="hidden md:inline-flex items-center justify-center w-9 h-9 rounded-lg text-slate-500 hover:bg-slate-100" aria-label="Ayuda">
                <HelpCircle class="w-5 h-5" />
            </button>

            <div class="relative">
                <button type="button" class="flex items-center gap-2 px-2 py-1.5 rounded-lg hover:bg-slate-100" @click="menuOpen = !menuOpen">
                    <Avatar v-if="auth.userId !== null" :user-id="auth.userId" :full-name="auth.fullName" :has-avatar="me?.hasAvatar ?? false" :size="32" :show-status="true" :online="true" />
                    <span class="hidden md:flex flex-col leading-tight text-left">
                        <strong class="text-sm text-slate-900">{{ auth.fullName }}</strong>
                        <span class="text-[11px] text-emerald-600">Disponible</span>
                    </span>
                    <ChevronDown class="w-4 h-4 text-slate-400" />
                </button>

                <div v-if="menuOpen" class="absolute right-0 mt-2 w-56 bg-white border border-slate-200 rounded-xl shadow-lg z-30 overflow-hidden">
                    <button type="button" class="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-slate-700 hover:bg-slate-50" @click="openAvatarPicker">
                        <ImageIcon class="w-4 h-4 text-slate-500" />
                        Cambiar foto de perfil
                    </button>
                    <button type="button" class="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-red-600 hover:bg-red-50 border-t border-slate-100" @click="logout">
                        <LogOut class="w-4 h-4" />
                        Cerrar sesión
                    </button>
                </div>

                <input ref="avatarFileInput" type="file" accept="image/png,image/jpeg,image/webp" class="hidden" @change="onAvatarSelected" />
            </div>
        </div>
    </header>
</template>
