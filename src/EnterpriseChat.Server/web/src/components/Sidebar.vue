<script setup lang="ts">
import { computed } from "vue";
import { useRoute, useRouter } from "vue-router";
import { useAuthStore } from "@/stores/auth";
import { useRoomsStore } from "@/stores/rooms";
import { useUsersStore } from "@/stores/users";

const auth = useAuthStore();
const rooms = useRoomsStore();
const users = useUsersStore();
const route = useRoute();
const router = useRouter();

const otherUsers = computed(() => users.users.filter((u) => u.id !== auth.userId));

function navigateRoom(id: number): void {
    void router.push({ name: "channel", params: { roomId: String(id) } });
}

function navigateDm(id: number): void {
    void router.push({ name: "dm", params: { peerUserId: String(id) } });
}

function isActiveRoom(id: number): boolean {
    return route.name === "channel" && route.params.roomId === String(id);
}

function isActiveDm(id: number): boolean {
    return route.name === "dm" && route.params.peerUserId === String(id);
}

async function newRoom(): Promise<void> {
    const name = window.prompt("Nombre del canal");
    if (name === null || name.trim() === "") return;
    const isPrivate = window.confirm("¿Canal privado? (Aceptar = sí)");
    const id = await rooms.create(name.trim(), isPrivate);
    navigateRoom(id);
}
</script>

<template>
    <nav class="h-full overflow-y-auto bg-white py-4 px-3 flex flex-col gap-6">
        <section>
            <header class="flex items-center justify-between px-2 mb-2">
                <span class="text-xs font-bold uppercase tracking-wider text-slate-500">Canales</span>
                <button type="button" class="text-slate-400 hover:text-slate-700 text-lg leading-none" @click="newRoom" aria-label="Nuevo canal">+</button>
            </header>
            <ul class="flex flex-col gap-1">
                <li v-for="r in rooms.memberRooms" :key="r.id">
                    <button
                        type="button"
                        @click="navigateRoom(r.id)"
                        :class="['w-full text-left px-2 py-1.5 rounded-md text-sm flex items-center gap-2',
                                 isActiveRoom(r.id) ? 'bg-blue-600 text-white font-semibold' : 'text-slate-700 hover:bg-slate-100']"
                    >
                        <span class="text-slate-400" :class="{ 'text-white/80': isActiveRoom(r.id) }">#</span>
                        <span class="flex-1 truncate">{{ r.name }}</span>
                        <span v-if="r.isPrivate" class="text-xs" :class="{ 'text-white/70': isActiveRoom(r.id), 'text-slate-400': !isActiveRoom(r.id) }">🔒</span>
                    </button>
                </li>
                <li v-if="rooms.memberRooms.length === 0" class="text-xs text-slate-400 px-2 py-1">
                    No estás en ningún canal aún.
                </li>
            </ul>
        </section>

        <section>
            <header class="flex items-center justify-between px-2 mb-2">
                <span class="text-xs font-bold uppercase tracking-wider text-slate-500">Mensajes directos</span>
            </header>
            <ul class="flex flex-col gap-1">
                <li v-for="u in otherUsers" :key="u.id">
                    <button
                        type="button"
                        @click="navigateDm(u.id)"
                        :class="['w-full text-left px-2 py-1.5 rounded-md text-sm flex items-center gap-2',
                                 isActiveDm(u.id) ? 'bg-blue-600 text-white font-semibold' : 'text-slate-700 hover:bg-slate-100']"
                    >
                        <span class="relative">
                            <span class="w-6 h-6 rounded-full bg-slate-200 grid place-items-center text-slate-700 text-[10px] font-bold">
                                {{ u.fullName.split(' ').map((p: string) => p[0]).slice(0, 2).join('').toUpperCase() }}
                            </span>
                            <span v-if="u.isOnline" class="absolute -bottom-0.5 -right-0.5 w-2 h-2 rounded-full bg-emerald-500 border-2 border-white"></span>
                        </span>
                        <span class="flex-1 truncate">{{ u.fullName }}</span>
                    </button>
                </li>
                <li v-if="otherUsers.length === 0" class="text-xs text-slate-400 px-2 py-1">
                    No hay otros usuarios.
                </li>
            </ul>
        </section>
    </nav>
</template>
