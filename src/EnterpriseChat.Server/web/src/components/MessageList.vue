<script setup lang="ts">
import { computed, nextTick, ref, watch } from "vue";
import { useAuthStore } from "@/stores/auth";
import { useUsersStore } from "@/stores/users";
import { useMessagesStore } from "@/stores/messages";
import { chatHub } from "@/services/signalr";
import type { ChatMessage, ThreadKey } from "@/api/types";
import { threadKeyId } from "@/api/types";

const props = defineProps<{ thread: ThreadKey }>();

const auth = useAuthStore();
const users = useUsersStore();
const messages = useMessagesStore();

const scrollEl = ref<HTMLDivElement | null>(null);
const items = computed(() => messages.get(props.thread));

const typingSet = computed(() => messages.typingByThread[threadKeyId(props.thread)] ?? new Set<number>());
const typingNames = computed(() => {
    return [...typingSet.value]
        .filter((id) => id !== auth.userId)
        .map((id) => users.findById(id)?.fullName ?? "Alguien")
        .slice(0, 3);
});

function authorName(m: ChatMessage): string {
    if (m.fromUserId === auth.userId) return "Tú";
    return users.findById(m.fromUserId)?.fullName ?? "Usuario";
}

function isOwn(m: ChatMessage): boolean {
    return m.fromUserId === auth.userId;
}

function timestamp(s: string): string {
    return new Date(s).toLocaleTimeString("es-ES", { hour: "2-digit", minute: "2-digit" });
}

function readState(m: ChatMessage): "sent" | "read" {
    if (m.serverId === null || m.serverId === undefined) return "sent";
    const peerCursor = props.thread.kind === "dm"
        ? messages.readCursors[`u:${props.thread.peerUserId}`] ?? 0
        : 0;
    return peerCursor >= m.serverId ? "read" : "sent";
}

watch(items, async () => {
    await nextTick();
    if (scrollEl.value !== null) {
        scrollEl.value.scrollTop = scrollEl.value.scrollHeight;
    }
    // Mark the latest visible message as read so the sender sees ✓✓.
    const last = items.value[items.value.length - 1];
    if (last !== undefined && last.serverId !== null && last.serverId !== undefined && last.fromUserId !== auth.userId) {
        try { await chatHub.markAsRead(last.serverId); } catch { /* ignore */ }
    }
}, { deep: true, flush: "post" });
</script>

<template>
    <div ref="scrollEl" class="h-full overflow-y-auto px-6 py-4 flex flex-col gap-3">
        <div v-if="items.length === 0" class="m-auto text-slate-400 text-sm">
            Sin mensajes todavía. Escribe algo abajo.
        </div>

        <article v-for="m in items" :key="m.messageId" class="flex" :class="isOwn(m) ? 'justify-end' : 'justify-start'">
            <div :class="['max-w-[70%] rounded-2xl px-4 py-2 text-sm shadow-sm',
                          isOwn(m) ? 'bg-blue-600 text-white' : 'bg-slate-100 text-slate-900']">
                <header class="flex items-baseline gap-2 mb-0.5">
                    <strong :class="['text-xs', isOwn(m) ? 'text-white/90' : 'text-slate-700']">{{ authorName(m) }}</strong>
                    <span :class="['text-[10px]', isOwn(m) ? 'text-white/70' : 'text-slate-500']">{{ timestamp(m.sentAt) }}</span>
                </header>
                <p class="whitespace-pre-line break-words">{{ m.body }}</p>
                <p v-if="m.attachmentFileName" class="text-xs mt-1" :class="isOwn(m) ? 'text-white/80' : 'text-blue-600'">
                    📎 <a :href="`/files/${m.attachmentId}`" target="_blank" rel="noopener" class="underline">{{ m.attachmentFileName }}</a>
                </p>
                <footer v-if="isOwn(m)" class="text-[10px] text-white/70 text-right mt-0.5">
                    {{ readState(m) === 'read' ? '✓✓' : '✓' }}
                </footer>
            </div>
        </article>

        <div v-if="typingNames.length > 0" class="text-xs text-slate-500 italic px-2">
            {{ typingNames.join(", ") }} {{ typingNames.length === 1 ? "está" : "están" }} escribiendo…
        </div>
    </div>
</template>
