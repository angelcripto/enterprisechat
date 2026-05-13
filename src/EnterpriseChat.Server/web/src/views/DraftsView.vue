<script setup lang="ts">
import { computed } from "vue";
import { useRouter } from "vue-router";
import { FileText, Hash, Trash2 } from "lucide-vue-next";
import Avatar from "@/components/Avatar.vue";
import { useDraftsStore } from "@/stores/drafts";
import { useRoomsStore } from "@/stores/rooms";
import { useUsersStore } from "@/stores/users";
import type { ThreadKey } from "@/api/types";

const drafts = useDraftsStore();
const rooms = useRoomsStore();
const users = useUsersStore();
const router = useRouter();

const items = computed(() => drafts.entries());

function targetTitle(key: ThreadKey): string {
    if (key.kind === "room") return `# ${rooms.findById(key.roomId)?.name ?? "canal"}`;
    return users.findById(key.peerUserId)?.fullName ?? "Usuario";
}

function go(key: ThreadKey): void {
    if (key.kind === "room") {
        void router.push({ name: "channel", params: { roomId: String(key.roomId) } });
    } else {
        void router.push({ name: "dm", params: { peerUserId: String(key.peerUserId) } });
    }
}

function timeAgo(timestamp: number): string {
    const mins = Math.floor((Date.now() - timestamp) / 60000);
    if (mins < 1) return "ahora";
    if (mins < 60) return `hace ${mins} min`;
    const hrs = Math.floor(mins / 60);
    if (hrs < 24) return `hace ${hrs} h`;
    return `hace ${Math.floor(hrs / 24)} d`;
}
</script>

<template>
    <div class="h-full overflow-y-auto bg-slate-50">
        <header class="sticky top-0 bg-white border-b border-slate-200 px-6 py-3 flex items-center gap-2">
            <FileText class="w-5 h-5 text-slate-500" />
            <h1 class="text-base font-semibold text-slate-900">Borradores</h1>
        </header>
        <div class="max-w-3xl mx-auto p-6">
            <p class="text-sm text-slate-500 mb-4">Mensajes que empezaste a escribir y dejaste a medias. Se guardan solo en este navegador.</p>
            <ul v-if="items.length > 0" class="flex flex-col gap-3">
                <li v-for="it in items" :key="`${it.key.kind}:${it.key.kind === 'room' ? it.key.roomId : it.key.peerUserId}`" class="bg-white rounded-xl border border-slate-200 p-4">
                    <header class="flex items-center gap-2 mb-2">
                        <Hash v-if="it.key.kind === 'room'" class="w-5 h-5 text-slate-400" />
                        <Avatar v-else :user-id="it.key.peerUserId" :full-name="targetTitle(it.key)" :has-avatar="users.findById(it.key.peerUserId)?.hasAvatar ?? false" :size="22" />
                        <strong class="text-sm text-slate-900">{{ targetTitle(it.key) }}</strong>
                        <span class="text-xs text-slate-400 ml-auto">{{ timeAgo(it.updatedAt) }}</span>
                    </header>
                    <p class="text-sm text-slate-700 whitespace-pre-line mb-2">{{ it.body }}</p>
                    <div class="flex gap-2">
                        <button type="button" class="text-xs text-blue-600 hover:text-blue-800" @click="go(it.key)">Continuar →</button>
                        <button type="button" class="text-xs text-red-600 hover:text-red-800 ml-auto inline-flex items-center gap-1" @click="drafts.clear(it.key)">
                            <Trash2 class="w-3 h-3" />
                            Descartar
                        </button>
                    </div>
                </li>
            </ul>
            <div v-else class="bg-white rounded-xl border border-slate-200 p-10 text-center text-sm text-slate-500">
                No hay borradores. Cuando empieces a escribir un mensaje y cambies de canal, lo encontrarás aquí.
            </div>
        </div>
    </div>
</template>
