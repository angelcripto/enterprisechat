<script setup lang="ts">
import { onMounted } from "vue";
import { useRouter } from "vue-router";
import { Inbox, Hash, Lock } from "lucide-vue-next";
import Avatar from "@/components/Avatar.vue";
import { useInboxStore } from "@/stores/inbox";
import { useUsersStore } from "@/stores/users";

const inbox = useInboxStore();
const users = useUsersStore();
const router = useRouter();

onMounted(async () => {
    try { await inbox.load(); } catch { /* endpoint puede no existir todavía */ }
});

function go(entry: { kind: string; roomId: number | null; peerUserId: number | null }): void {
    if (entry.kind === "room" && entry.roomId !== null) {
        void router.push({ name: "channel", params: { roomId: String(entry.roomId) } });
    } else if (entry.kind === "dm" && entry.peerUserId !== null) {
        void router.push({ name: "dm", params: { peerUserId: String(entry.peerUserId) } });
    }
}

function timeAgo(iso: string): string {
    const diff = Date.now() - new Date(iso).getTime();
    const mins = Math.floor(diff / 60000);
    if (mins < 1) return "ahora";
    if (mins < 60) return `${mins} min`;
    const hrs = Math.floor(mins / 60);
    if (hrs < 24) return `${hrs} h`;
    return `${Math.floor(hrs / 24)} d`;
}
</script>

<template>
    <div class="h-full overflow-y-auto bg-slate-50">
        <header class="sticky top-0 bg-white border-b border-slate-200 px-6 py-3 flex items-center gap-2">
            <Inbox class="w-5 h-5 text-slate-500" />
            <h1 class="text-base font-semibold text-slate-900">Inicio</h1>
        </header>
        <div class="max-w-3xl mx-auto p-6">
            <p class="text-sm text-slate-500 mb-4">Conversaciones recientes ordenadas por última actividad.</p>
            <div v-if="inbox.loading" class="text-sm text-slate-500">Cargando…</div>
            <ul v-else-if="inbox.items.length > 0" class="flex flex-col gap-1 bg-white rounded-xl border border-slate-200 divide-y divide-slate-100">
                <li v-for="entry in inbox.items" :key="`${entry.kind}:${entry.roomId ?? entry.peerUserId}`">
                    <button type="button" @click="go(entry)" class="w-full text-left px-4 py-3 flex items-center gap-3 hover:bg-slate-50">
                        <component v-if="entry.kind === 'room'" :is="entry.isPrivate ? Lock : Hash" class="w-8 h-8 p-1.5 rounded-md bg-slate-100 text-slate-600" />
                        <Avatar v-else-if="entry.peerUserId !== null" :user-id="entry.peerUserId" :full-name="entry.title" :has-avatar="users.findById(entry.peerUserId)?.hasAvatar ?? false" :size="32" />
                        <span class="flex-1 min-w-0">
                            <span class="flex items-center gap-2">
                                <strong class="text-sm text-slate-900 truncate">{{ entry.title }}</strong>
                                <span class="text-[11px] text-slate-400 ml-auto">{{ timeAgo(entry.lastAt) }}</span>
                            </span>
                            <span class="block text-xs text-slate-500 truncate">{{ entry.lastBody }}</span>
                        </span>
                        <span v-if="entry.unreadCount > 0" class="text-[10px] bg-blue-600 text-white px-1.5 py-0.5 rounded-full font-bold">{{ entry.unreadCount }}</span>
                    </button>
                </li>
            </ul>
            <div v-else class="bg-white rounded-xl border border-slate-200 p-10 text-center text-sm text-slate-500">
                No hay conversaciones recientes.
            </div>
        </div>
    </div>
</template>
