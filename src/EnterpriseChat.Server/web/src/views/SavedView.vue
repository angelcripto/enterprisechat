<script setup lang="ts">
import { onMounted } from "vue";
import { useRouter } from "vue-router";
import { Bookmark } from "lucide-vue-next";
import Avatar from "@/components/Avatar.vue";
import { useSavedStore } from "@/stores/saved";
import { useUsersStore } from "@/stores/users";

const saved = useSavedStore();
const users = useUsersStore();
const router = useRouter();

onMounted(async () => {
    try { await saved.load(); } catch { /* puede que el endpoint no exista aún */ }
});

function authorName(userId: number): string {
    return users.findById(userId)?.fullName ?? "Usuario";
}

function goTo(m: { roomId: number | null | undefined; fromUserId: number }): void {
    if (m.roomId !== null && m.roomId !== undefined) {
        void router.push({ name: "channel", params: { roomId: String(m.roomId) } });
    } else {
        void router.push({ name: "dm", params: { peerUserId: String(m.fromUserId) } });
    }
}

async function unsave(messageId: number): Promise<void> {
    try { await saved.toggle(messageId); } catch { /* ignore */ }
}
</script>

<template>
    <div class="h-full overflow-y-auto bg-slate-50">
        <header class="sticky top-0 bg-white border-b border-slate-200 px-6 py-3 flex items-center gap-2">
            <Bookmark class="w-5 h-5 text-slate-500" />
            <h1 class="text-base font-semibold text-slate-900">Guardados</h1>
        </header>
        <div class="max-w-3xl mx-auto p-6">
            <p class="text-sm text-slate-500 mb-4">Mensajes que has guardado para volver a ellos más tarde.</p>
            <div v-if="saved.loading" class="text-sm text-slate-500">Cargando…</div>
            <ul v-else-if="saved.items.length > 0" class="flex flex-col gap-3">
                <li v-for="m in saved.items" :key="m.messageId" class="bg-white rounded-xl border border-slate-200 p-4">
                    <header class="flex items-center gap-2 mb-2">
                        <Avatar :user-id="m.fromUserId" :full-name="authorName(m.fromUserId)" :has-avatar="users.findById(m.fromUserId)?.hasAvatar ?? false" :size="28" />
                        <strong class="text-sm text-slate-900">{{ authorName(m.fromUserId) }}</strong>
                        <span class="text-xs text-slate-500">{{ new Date(m.sentAt).toLocaleString('es-ES') }}</span>
                        <span class="text-xs text-slate-400 ml-auto">guardado {{ new Date(m.savedAt).toLocaleDateString('es-ES') }}</span>
                    </header>
                    <p class="text-sm text-slate-700 whitespace-pre-line mb-2">{{ m.body }}</p>
                    <div class="flex gap-2">
                        <button type="button" class="text-xs text-blue-600 hover:text-blue-800" @click="goTo(m)">Ir al mensaje →</button>
                        <button type="button" class="text-xs text-red-600 hover:text-red-800 ml-auto" @click="m.serverId !== null && m.serverId !== undefined && unsave(m.serverId)">Quitar de guardados</button>
                    </div>
                </li>
            </ul>
            <div v-else class="bg-white rounded-xl border border-slate-200 p-10 text-center text-sm text-slate-500">
                Aún no has guardado ningún mensaje. Haz click en el icono de marcador junto a un mensaje para guardarlo.
            </div>
        </div>
    </div>
</template>
