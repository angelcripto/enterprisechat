<script setup lang="ts">
import { computed } from "vue";
import { useRoute, useRouter } from "vue-router";
import { Hash, Lock, Plus, Inbox, AtSign, FileText, Bookmark, Users, Briefcase, ChevronDown } from "lucide-vue-next";
import Avatar from "@/components/Avatar.vue";
import { useAuthStore } from "@/stores/auth";
import { useRoomsStore } from "@/stores/rooms";
import { useUsersStore } from "@/stores/users";
import { useLicenseStore } from "@/stores/license";
import { isProEdition } from "@/api/types";
import { dialogPrompt, dialogConfirm, dialogError } from "@/dialogs";

const auth = useAuthStore();
const rooms = useRoomsStore();
const users = useUsersStore();
const license = useLicenseStore();
const route = useRoute();
const router = useRouter();

const otherUsers = computed(() => users.users.filter((u) => u.id !== auth.userId));
const departments = computed(() => {
    const names = new Set<string>();
    for (const u of users.users) {
        if (u.department) names.add(u.department);
    }
    return [...names].sort();
});

function navigateRoom(id: number): void { void router.push({ name: "channel", params: { roomId: String(id) } }); }
function navigateDm(id: number): void { void router.push({ name: "dm", params: { peerUserId: String(id) } }); }
function isActiveRoom(id: number): boolean { return route.name === "channel" && route.params.roomId === String(id); }
function isActiveDm(id: number): boolean { return route.name === "dm" && route.params.peerUserId === String(id); }

async function newRoom(): Promise<void> {
    const name = await dialogPrompt({
        title: "Nuevo canal",
        placeholder: "p. ej. ventas",
        validator: (v) => v.trim() === "" ? "Pon un nombre." : (v.length > 64 ? "Máximo 64 caracteres." : null),
    });
    if (name === null) return;
    const isPrivate = await dialogConfirm({
        title: "¿Canal privado?",
        text: "Los canales privados solo son visibles para los miembros invitados.",
        confirmText: "Privado",
        cancelText: "Público",
        icon: "question",
    });
    try {
        const id = await rooms.create(name.trim(), isPrivate);
        navigateRoom(id);
    } catch (err) {
        await dialogError("No se pudo crear el canal", err instanceof Error ? err.message : String(err));
    }
}
</script>

<template>
    <nav class="h-full overflow-y-auto bg-white flex flex-col">
        <header class="px-4 pt-4 pb-3 border-b border-slate-100">
            <button type="button" class="w-full flex items-center gap-2 px-2 py-2 rounded-lg hover:bg-slate-50 text-left">
                <span class="w-9 h-9 rounded-lg bg-blue-600 grid place-items-center text-white font-bold text-sm flex-shrink-0">EC</span>
                <span class="flex-1 min-w-0">
                    <strong class="block text-slate-900 truncate">{{ auth.fullName || 'EnterpriseChat' }}</strong>
                    <span v-if="isProEdition(license.info)" class="text-[10px] font-bold uppercase tracking-wider text-blue-600">Plan Pro</span>
                    <span v-else class="text-[10px] font-medium uppercase tracking-wider text-slate-400">Plan Free</span>
                </span>
                <ChevronDown class="w-4 h-4 text-slate-400 flex-shrink-0" />
            </button>
        </header>

        <div class="flex-1 overflow-y-auto px-3 py-3 flex flex-col gap-5">
            <ul class="flex flex-col gap-0.5">
                <li>
                    <button type="button" @click="router.push('/')" class="w-full flex items-center gap-2.5 px-2.5 py-1.5 rounded-md text-sm text-slate-700 hover:bg-slate-100">
                        <Inbox class="w-4 h-4 text-slate-500" />
                        <span class="flex-1 text-left">Inicio</span>
                    </button>
                </li>
                <li>
                    <button type="button" class="w-full flex items-center gap-2.5 px-2.5 py-1.5 rounded-md text-sm text-slate-700 hover:bg-slate-100">
                        <AtSign class="w-4 h-4 text-slate-500" />
                        <span class="flex-1 text-left">Menciones</span>
                    </button>
                </li>
                <li>
                    <button type="button" class="w-full flex items-center gap-2.5 px-2.5 py-1.5 rounded-md text-sm text-slate-700 hover:bg-slate-100">
                        <FileText class="w-4 h-4 text-slate-500" />
                        <span class="flex-1 text-left">Borradores</span>
                    </button>
                </li>
                <li>
                    <button type="button" class="w-full flex items-center gap-2.5 px-2.5 py-1.5 rounded-md text-sm text-slate-700 hover:bg-slate-100">
                        <Bookmark class="w-4 h-4 text-slate-500" />
                        <span class="flex-1 text-left">Guardados</span>
                    </button>
                </li>
            </ul>

            <section>
                <header class="flex items-center justify-between px-2 mb-1.5">
                    <span class="text-xs font-bold uppercase tracking-wider text-slate-500">Canales</span>
                    <button type="button" class="text-slate-400 hover:text-slate-700 p-0.5 rounded hover:bg-slate-100" @click="newRoom" aria-label="Nuevo canal">
                        <Plus class="w-4 h-4" />
                    </button>
                </header>
                <ul class="flex flex-col gap-0.5">
                    <li v-for="r in rooms.memberRooms" :key="r.id">
                        <button type="button" @click="navigateRoom(r.id)"
                            :class="['w-full text-left px-2.5 py-1.5 rounded-md text-sm flex items-center gap-2',
                                     isActiveRoom(r.id) ? 'bg-blue-600 text-white font-semibold' : 'text-slate-700 hover:bg-slate-100']">
                            <component :is="r.isPrivate ? Lock : Hash" :class="['w-4 h-4', isActiveRoom(r.id) ? 'text-white/80' : 'text-slate-400']" />
                            <span class="flex-1 truncate">{{ r.name }}</span>
                        </button>
                    </li>
                    <li v-if="rooms.memberRooms.length === 0" class="text-xs text-slate-400 px-2.5 py-1">
                        No estás en ningún canal aún.
                    </li>
                </ul>
            </section>

            <section>
                <header class="flex items-center justify-between px-2 mb-1.5">
                    <span class="text-xs font-bold uppercase tracking-wider text-slate-500">Mensajes directos</span>
                    <Plus class="w-4 h-4 text-slate-300" />
                </header>
                <ul class="flex flex-col gap-0.5">
                    <li v-for="u in otherUsers" :key="u.id">
                        <button type="button" @click="navigateDm(u.id)"
                            :class="['w-full text-left px-2.5 py-1.5 rounded-md text-sm flex items-center gap-2.5',
                                     isActiveDm(u.id) ? 'bg-blue-600 text-white font-semibold' : 'text-slate-700 hover:bg-slate-100']">
                            <Avatar :user-id="u.id" :full-name="u.fullName" :has-avatar="u.hasAvatar" :size="22" :show-status="true" :online="u.isOnline" />
                            <span class="flex-1 truncate">{{ u.fullName }}</span>
                        </button>
                    </li>
                    <li v-if="otherUsers.length === 0" class="text-xs text-slate-400 px-2.5 py-1">No hay otros usuarios.</li>
                </ul>
            </section>

            <section v-if="departments.length > 0">
                <header class="flex items-center justify-between px-2 mb-1.5">
                    <span class="text-xs font-bold uppercase tracking-wider text-slate-500">Equipos</span>
                    <Plus class="w-4 h-4 text-slate-300" />
                </header>
                <ul class="flex flex-col gap-0.5">
                    <li v-for="dept in departments" :key="dept">
                        <span class="w-full flex items-center gap-2 px-2.5 py-1.5 rounded-md text-sm text-slate-700">
                            <Briefcase class="w-4 h-4 text-slate-500" />
                            <span class="flex-1 truncate">{{ dept }}</span>
                        </span>
                    </li>
                </ul>
            </section>

            <section v-if="auth.isAdmin">
                <header class="flex items-center px-2 mb-1.5">
                    <span class="text-xs font-bold uppercase tracking-wider text-slate-500">Administración</span>
                </header>
                <ul class="flex flex-col gap-0.5">
                    <li>
                        <router-link :to="{ name: 'admin-license' }" class="w-full flex items-center gap-2.5 px-2.5 py-1.5 rounded-md text-sm text-slate-700 hover:bg-slate-100">
                            <Users class="w-4 h-4 text-slate-500" />
                            <span class="flex-1 text-left">Licencia</span>
                        </router-link>
                    </li>
                </ul>
            </section>
        </div>

        <footer class="m-3 mt-0 p-3 rounded-xl border border-slate-200 bg-gradient-to-br from-blue-50 to-white">
            <div class="text-xs text-slate-500 font-semibold uppercase tracking-wider mb-1">
                {{ isProEdition(license.info) ? 'Plan Pro' : 'Plan Free' }}
            </div>
            <div class="text-sm text-slate-900 font-medium mb-1">
                {{ isProEdition(license.info) && license.info?.licensedTo ? license.info.licensedTo : 'Hasta 10 usuarios' }}
            </div>
            <router-link v-if="auth.isAdmin" :to="{ name: 'admin-license' }" class="text-xs text-blue-600 hover:text-blue-800 font-medium">
                Gestionar licencia →
            </router-link>
        </footer>
    </nav>
</template>
