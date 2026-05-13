<script setup lang="ts">
import { computed, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { Hash, Lock, Plus, Inbox, AtSign, FileText, Bookmark, Briefcase, Shield } from "lucide-vue-next";
import Avatar from "@/components/Avatar.vue";
import WorkspaceMenu from "@/components/WorkspaceMenu.vue";
import CreateChannelModal from "@/components/CreateChannelModal.vue";
import InviteUserModal from "@/components/InviteUserModal.vue";
import { useAuthStore } from "@/stores/auth";
import { useRoomsStore } from "@/stores/rooms";
import { useUsersStore } from "@/stores/users";
import { useLicenseStore } from "@/stores/license";
import { useDraftsStore } from "@/stores/drafts";
import { isProEdition } from "@/api/types";

const auth = useAuthStore();
const rooms = useRoomsStore();
const users = useUsersStore();
const license = useLicenseStore();
const drafts = useDraftsStore();
const route = useRoute();
const router = useRouter();

const otherUsers = computed(() => users.users.filter((u) => u.id !== auth.userId));
const departments = computed(() => {
    const set = new Map<string, number>();
    for (const u of users.users) {
        if (u.department) set.set(u.department, (set.get(u.department) ?? 0) + 1);
    }
    return [...set.entries()].sort((a, b) => a[0].localeCompare(b[0]));
});

const draftCount = computed(() => drafts.count);

const channelModalOpen = ref(false);
const inviteModalOpen = ref(false);

function navigateRoom(id: number): void { void router.push({ name: "channel", params: { roomId: String(id) } }); }
function navigateDm(id: number): void { void router.push({ name: "dm", params: { peerUserId: String(id) } }); }
function isActiveRoom(id: number): boolean { return route.name === "channel" && route.params.roomId === String(id); }
function isActiveDm(id: number): boolean { return route.name === "dm" && route.params.peerUserId === String(id); }
function isActiveName(name: string): boolean { return route.name === name; }

function onChannelCreated(roomId: number): void {
    navigateRoom(roomId);
}
</script>

<template>
    <nav class="h-full overflow-y-auto bg-white flex flex-col">
        <header class="px-3 pt-3 pb-2 border-b border-slate-100">
            <WorkspaceMenu @invite="inviteModalOpen = true" />
        </header>

        <div class="flex-1 overflow-y-auto px-3 py-3 flex flex-col gap-5">
            <ul class="flex flex-col gap-0.5">
                <li>
                    <router-link :to="{ name: 'inbox' }"
                        :class="['w-full flex items-center gap-2.5 px-2.5 py-1.5 rounded-md text-sm',
                                 isActiveName('inbox') ? 'bg-blue-50 text-blue-700 font-semibold' : 'text-slate-700 hover:bg-slate-100']">
                        <Inbox class="w-4 h-4 text-slate-500" />
                        <span class="flex-1 text-left">Inicio</span>
                    </router-link>
                </li>
                <li>
                    <router-link :to="{ name: 'mentions' }"
                        :class="['w-full flex items-center gap-2.5 px-2.5 py-1.5 rounded-md text-sm',
                                 isActiveName('mentions') ? 'bg-blue-50 text-blue-700 font-semibold' : 'text-slate-700 hover:bg-slate-100']">
                        <AtSign class="w-4 h-4 text-slate-500" />
                        <span class="flex-1 text-left">Menciones</span>
                    </router-link>
                </li>
                <li>
                    <router-link :to="{ name: 'drafts' }"
                        :class="['w-full flex items-center gap-2.5 px-2.5 py-1.5 rounded-md text-sm',
                                 isActiveName('drafts') ? 'bg-blue-50 text-blue-700 font-semibold' : 'text-slate-700 hover:bg-slate-100']">
                        <FileText class="w-4 h-4 text-slate-500" />
                        <span class="flex-1 text-left">Borradores</span>
                        <span v-if="draftCount > 0" class="ml-auto text-[10px] bg-slate-200 text-slate-700 px-1.5 py-0.5 rounded-full font-semibold">{{ draftCount }}</span>
                    </router-link>
                </li>
                <li>
                    <router-link :to="{ name: 'saved' }"
                        :class="['w-full flex items-center gap-2.5 px-2.5 py-1.5 rounded-md text-sm',
                                 isActiveName('saved') ? 'bg-blue-50 text-blue-700 font-semibold' : 'text-slate-700 hover:bg-slate-100']">
                        <Bookmark class="w-4 h-4 text-slate-500" />
                        <span class="flex-1 text-left">Guardados</span>
                    </router-link>
                </li>
            </ul>

            <section>
                <header class="flex items-center justify-between px-2 mb-1.5">
                    <span class="text-xs font-bold uppercase tracking-wider text-slate-500">Canales</span>
                    <button type="button" class="text-slate-400 hover:text-slate-700 p-0.5 rounded hover:bg-slate-100" @click="channelModalOpen = true" aria-label="Nuevo canal">
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
                    <li v-if="rooms.memberRooms.length === 0" class="text-xs text-slate-400 px-2.5 py-1">No estás en ningún canal aún.</li>
                </ul>
            </section>

            <section>
                <header class="flex items-center justify-between px-2 mb-1.5">
                    <span class="text-xs font-bold uppercase tracking-wider text-slate-500">Mensajes directos</span>
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
                </header>
                <ul class="flex flex-col gap-0.5">
                    <li v-for="[dept, count] in departments" :key="dept">
                        <router-link :to="{ name: 'team', params: { name: dept } }"
                            :class="['w-full flex items-center gap-2 px-2.5 py-1.5 rounded-md text-sm',
                                     route.name === 'team' && route.params.name === dept ? 'bg-blue-50 text-blue-700 font-semibold' : 'text-slate-700 hover:bg-slate-100']">
                            <Briefcase class="w-4 h-4 text-slate-500" />
                            <span class="flex-1 truncate">{{ dept }}</span>
                            <span class="text-[10px] text-slate-400 font-mono">{{ count }}</span>
                        </router-link>
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
                            <Shield class="w-4 h-4 text-slate-500" />
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
                {{ isProEdition(license.info) ? 'Gestionar licencia' : 'Activar Pro' }} →
            </router-link>
        </footer>

        <CreateChannelModal v-if="channelModalOpen" @close="channelModalOpen = false" @created="onChannelCreated" />
        <InviteUserModal v-if="inviteModalOpen" @close="inviteModalOpen = false" @invited="users.load()" />
    </nav>
</template>
