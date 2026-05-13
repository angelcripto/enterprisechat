<script setup lang="ts">
import { computed } from "vue";
import { useRoomsStore } from "@/stores/rooms";
import { useUsersStore } from "@/stores/users";
import { useMessagesStore } from "@/stores/messages";
import type { ThreadKey } from "@/api/types";
import { threadKeyId } from "@/api/types";
import MessageList from "./MessageList.vue";
import MessageInput from "./MessageInput.vue";

const props = defineProps<{ thread: ThreadKey }>();

const rooms = useRoomsStore();
const users = useUsersStore();
const messages = useMessagesStore();

const headerTitle = computed(() => {
    if (props.thread.kind === "room") {
        const r = rooms.findById(props.thread.roomId);
        return r?.name ?? "Canal";
    }
    const u = users.findById(props.thread.peerUserId);
    return u?.fullName ?? "Usuario";
});

const headerSubtitle = computed(() => {
    if (props.thread.kind === "room") {
        const r = rooms.findById(props.thread.roomId);
        if (r === undefined) return "";
        return `${r.memberCount} miembro${r.memberCount === 1 ? "" : "s"} · ${r.isPrivate ? "privado" : "público"}`;
    }
    const u = users.findById(props.thread.peerUserId);
    return u?.isOnline === true ? "En línea" : "Desconectado";
});

const isLoading = computed(() => messages.loadingThreads[threadKeyId(props.thread)] === true);
</script>

<template>
    <div class="flex flex-col h-full bg-white">
        <header class="px-6 py-3 border-b border-slate-200 flex items-center gap-3">
            <div class="flex flex-col leading-tight">
                <strong class="text-slate-900">
                    <span v-if="thread.kind === 'room'" class="text-slate-400">#</span>
                    {{ headerTitle }}
                </strong>
                <span class="text-xs text-slate-500">{{ headerSubtitle }}</span>
            </div>
        </header>

        <div class="flex-1 overflow-hidden">
            <div v-if="isLoading" class="h-full grid place-items-center text-sm text-slate-500">Cargando historial…</div>
            <MessageList v-else :thread="thread" />
        </div>

        <MessageInput :thread="thread" />
    </div>
</template>
