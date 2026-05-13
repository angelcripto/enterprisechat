<script setup lang="ts">
import { computed, nextTick, ref, watch } from "vue";
import { Paperclip, Download } from "lucide-vue-next";
import { useAuthStore } from "@/stores/auth";
import { useUsersStore } from "@/stores/users";
import { useMessagesStore } from "@/stores/messages";
import { chatHub } from "@/services/signalr";
import Avatar from "@/components/Avatar.vue";
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

function author(m: ChatMessage) {
    return users.findById(m.fromUserId);
}

function authorName(m: ChatMessage): string {
    if (m.fromUserId === auth.userId) return "Tú";
    return author(m)?.fullName ?? "Usuario";
}

function timestamp(s: string): string {
    return new Date(s).toLocaleTimeString("es-ES", { hour: "2-digit", minute: "2-digit" });
}

function isOwn(m: ChatMessage): boolean {
    return m.fromUserId === auth.userId;
}

function readState(m: ChatMessage): "sent" | "read" {
    if (m.serverId === null || m.serverId === undefined) return "sent";
    const peerCursor = props.thread.kind === "dm"
        ? messages.readCursors[`u:${props.thread.peerUserId}`] ?? 0
        : 0;
    return peerCursor >= m.serverId ? "read" : "sent";
}

function fmtSize(bytes: number | null | undefined): string {
    if (bytes === null || bytes === undefined || bytes === 0) return "";
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

watch(items, async () => {
    await nextTick();
    if (scrollEl.value !== null) {
        scrollEl.value.scrollTop = scrollEl.value.scrollHeight;
    }
    const last = items.value[items.value.length - 1];
    if (last !== undefined && last.serverId !== null && last.serverId !== undefined && last.fromUserId !== auth.userId) {
        try { await chatHub.markAsRead(last.serverId); } catch { /* ignore */ }
    }
}, { deep: true, flush: "post" });
</script>

<template>
    <div ref="scrollEl" class="h-full overflow-y-auto px-6 py-4 flex flex-col gap-1">
        <div v-if="items.length === 0" class="m-auto text-slate-400 text-sm">
            Sin mensajes todavía. Escribe algo abajo para iniciar la conversación.
        </div>

        <div v-for="(m, idx) in items" :key="m.messageId" class="group">
            <article class="flex gap-3 px-2 py-1.5 rounded-lg hover:bg-slate-50">
                <div class="pt-0.5">
                    <Avatar
                        v-if="idx === 0 || items[idx - 1].fromUserId !== m.fromUserId"
                        :user-id="m.fromUserId"
                        :full-name="authorName(m)"
                        :has-avatar="author(m)?.hasAvatar ?? false"
                        :size="36"
                    />
                    <span v-else class="block w-9 text-center text-[10px] text-slate-300 opacity-0 group-hover:opacity-100">{{ timestamp(m.sentAt) }}</span>
                </div>
                <div class="flex-1 min-w-0">
                    <header v-if="idx === 0 || items[idx - 1].fromUserId !== m.fromUserId" class="flex items-baseline gap-2 mb-0.5">
                        <strong class="text-sm text-slate-900">{{ authorName(m) }}</strong>
                        <span class="text-[11px] text-slate-500">{{ timestamp(m.sentAt) }}</span>
                    </header>
                    <p class="text-sm text-slate-900 whitespace-pre-line break-words">{{ m.body }}</p>

                    <a v-if="m.attachmentFileName" :href="`/files/${m.attachmentId}`" target="_blank" rel="noopener"
                        class="mt-1.5 inline-flex items-center gap-2.5 px-3 py-2 bg-white border border-slate-200 rounded-lg hover:bg-slate-50 max-w-md">
                        <Paperclip class="w-4 h-4 text-slate-500 flex-shrink-0" />
                        <span class="flex-1 min-w-0">
                            <span class="block text-sm text-slate-900 font-medium truncate">{{ m.attachmentFileName }}</span>
                            <span class="block text-[11px] text-slate-500">{{ fmtSize(m.attachmentSizeBytes) }}</span>
                        </span>
                        <Download class="w-4 h-4 text-slate-400" />
                    </a>

                    <footer v-if="isOwn(m)" class="text-[10px] text-slate-400 mt-0.5">
                        {{ readState(m) === 'read' ? '✓✓ leído' : '✓ enviado' }}
                    </footer>
                </div>
            </article>
        </div>

        <div v-if="typingNames.length > 0" class="text-xs text-slate-500 italic px-3 mt-1 flex items-center gap-2">
            <span class="flex gap-0.5">
                <span class="w-1.5 h-1.5 rounded-full bg-slate-400 animate-bounce" style="animation-delay:0ms;"></span>
                <span class="w-1.5 h-1.5 rounded-full bg-slate-400 animate-bounce" style="animation-delay:150ms;"></span>
                <span class="w-1.5 h-1.5 rounded-full bg-slate-400 animate-bounce" style="animation-delay:300ms;"></span>
            </span>
            {{ typingNames.join(", ") }} {{ typingNames.length === 1 ? "está" : "están" }} escribiendo…
        </div>
    </div>
</template>
