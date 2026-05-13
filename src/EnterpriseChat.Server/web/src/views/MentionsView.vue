<script setup lang="ts">
import { onMounted } from "vue";
import { useRouter } from "vue-router";
import { AtSign } from "lucide-vue-next";
import Avatar from "@/components/Avatar.vue";
import { useMentionsStore } from "@/stores/mentions";
import { useUsersStore } from "@/stores/users";

const mentions = useMentionsStore();
const users = useUsersStore();
const router = useRouter();

onMounted(async () => {
    try { await mentions.load(); } catch { /* puede que el endpoint no exista aún */ }
});

function authorName(userId: number): string {
    return users.findById(userId)?.fullName ?? "Usuario";
}

function authorHasAvatar(userId: number): boolean {
    return users.findById(userId)?.hasAvatar ?? false;
}

function goTo(m: { roomId: number | null | undefined; fromUserId: number }): void {
    if (m.roomId !== null && m.roomId !== undefined) {
        void router.push({ name: "channel", params: { roomId: String(m.roomId) } });
    } else {
        void router.push({ name: "dm", params: { peerUserId: String(m.fromUserId) } });
    }
}
</script>

<template>
    <div class="h-full overflow-y-auto bg-slate-50">
        <header class="sticky top-0 bg-white border-b border-slate-200 px-6 py-3 flex items-center gap-2">
            <AtSign class="w-5 h-5 text-slate-500" />
            <h1 class="text-base font-semibold text-slate-900">Menciones</h1>
        </header>
        <div class="max-w-3xl mx-auto p-6">
            <p class="text-sm text-slate-500 mb-4">Mensajes en los que alguien te ha mencionado con @.</p>
            <div v-if="mentions.loading" class="text-sm text-slate-500">Cargando…</div>
            <ul v-else-if="mentions.items.length > 0" class="flex flex-col gap-3">
                <li v-for="m in mentions.items" :key="m.messageId" class="bg-white rounded-xl border border-slate-200 p-4 hover:border-blue-300 cursor-pointer" @click="goTo(m)">
                    <header class="flex items-center gap-2 mb-2">
                        <Avatar :user-id="m.fromUserId" :full-name="authorName(m.fromUserId)" :has-avatar="authorHasAvatar(m.fromUserId)" :size="28" />
                        <strong class="text-sm text-slate-900">{{ authorName(m.fromUserId) }}</strong>
                        <span class="text-xs text-slate-500">{{ new Date(m.sentAt).toLocaleString('es-ES') }}</span>
                    </header>
                    <p class="text-sm text-slate-700 whitespace-pre-line">{{ m.body }}</p>
                </li>
            </ul>
            <div v-else class="bg-white rounded-xl border border-slate-200 p-10 text-center text-sm text-slate-500">
                No tienes menciones todavía.
            </div>
        </div>
    </div>
</template>
