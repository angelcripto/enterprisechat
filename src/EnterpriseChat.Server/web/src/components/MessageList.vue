<script setup lang="ts">
import { computed, nextTick, ref, watch } from "vue";
import { Paperclip, Download } from "lucide-vue-next";
import { useAuthStore } from "@/stores/auth";
import { useUsersStore } from "@/stores/users";
import { useMessagesStore } from "@/stores/messages";
import { chatHub } from "@/services/signalr";
import { getAccessToken } from "@/api/client";
import Avatar from "@/components/Avatar.vue";
import type { ChatMessage, ThreadKey } from "@/api/types";
import { threadKeyId } from "@/api/types";

/** /files/{id} requires auth and <img>/<a> tags can't set Authorization
 *  headers, so the JwtBearer pipeline accepts the bearer as ?access_token=
 *  query string (same trick SignalR uses on /hubs). */
function fileUrl(id: number | null | undefined): string {
    if (id === null || id === undefined) return "#";
    const token = getAccessToken();
    return token === null ? `/files/${id}` : `/files/${id}?access_token=${encodeURIComponent(token)}`;
}

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

const IMAGE_EXTS = new Set(["png", "jpg", "jpeg", "gif", "webp", "bmp", "avif", "svg"]);

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

function isImageAttachment(m: ChatMessage): boolean {
    if (!m.attachmentFileName) return false;
    const ext = m.attachmentFileName.split(".").pop()?.toLowerCase() ?? "";
    return IMAGE_EXTS.has(ext);
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
    <div ref="scrollEl" class="h-full overflow-y-auto px-6 py-4 flex flex-col gap-2">
        <div v-if="items.length === 0" class="m-auto text-slate-400 text-sm">
            Sin mensajes todavía. Escribe algo abajo para iniciar la conversación.
        </div>

        <article
            v-for="m in items"
            :key="m.messageId"
            :class="['flex items-end gap-2 max-w-full', isOwn(m) ? 'flex-row-reverse self-end' : 'self-start']"
            style="max-width: 75%"
        >
            <Avatar
                :user-id="m.fromUserId"
                :full-name="authorName(m)"
                :has-avatar="author(m)?.hasAvatar ?? false"
                :size="32"
                class="flex-shrink-0"
            />
            <div :class="['flex flex-col min-w-0', isOwn(m) ? 'items-end' : 'items-start']">
                <header class="flex items-baseline gap-2 mb-0.5 px-1">
                    <strong class="text-xs text-slate-700">{{ authorName(m) }}</strong>
                    <span class="text-[10px] text-slate-400">{{ timestamp(m.sentAt) }}</span>
                </header>

                <!-- Image bubble: shows the picture itself, body underneath if any -->
                <a
                    v-if="isImageAttachment(m)"
                    :href="fileUrl(m.attachmentId)"
                    target="_blank"
                    rel="noopener"
                    :class="['block rounded-2xl overflow-hidden border shadow-sm max-w-sm',
                             isOwn(m) ? 'border-blue-200 rounded-br-md' : 'border-slate-200 rounded-bl-md']"
                >
                    <img
                        :src="fileUrl(m.attachmentId)"
                        :alt="m.attachmentFileName ?? 'imagen'"
                        class="block w-full max-h-80 object-cover bg-slate-100"
                        loading="lazy"
                    />
                    <div v-if="m.body" :class="['px-3 py-2 text-sm',
                                                 isOwn(m) ? 'bg-blue-600 text-white' : 'bg-slate-100 text-slate-900']">
                        {{ m.body }}
                    </div>
                    <div :class="['flex items-center gap-2 px-3 py-1.5 text-[11px]',
                                  isOwn(m) ? 'bg-blue-600 text-white/80' : 'bg-slate-100 text-slate-500']">
                        <Paperclip class="w-3 h-3" />
                        <span class="flex-1 truncate">{{ m.attachmentFileName }}</span>
                        <span>{{ fmtSize(m.attachmentSizeBytes) }}</span>
                    </div>
                </a>

                <!-- Regular bubble (text + optional non-image file card inside) -->
                <div
                    v-else
                    :class="['px-3.5 py-2 text-sm whitespace-pre-line break-words shadow-sm',
                             isOwn(m)
                                ? 'bg-blue-600 text-white rounded-2xl rounded-br-md'
                                : 'bg-white border border-slate-200 text-slate-900 rounded-2xl rounded-bl-md']"
                >
                    <p v-if="m.body" class="m-0">{{ m.body }}</p>
                    <a
                        v-if="m.attachmentFileName"
                        :href="fileUrl(m.attachmentId)"
                        target="_blank"
                        rel="noopener"
                        :class="['mt-1.5 inline-flex items-center gap-2 px-2.5 py-1.5 rounded-lg max-w-full',
                                 isOwn(m) ? 'bg-blue-700/40 hover:bg-blue-700/60' : 'bg-slate-50 hover:bg-slate-100 border border-slate-200']"
                    >
                        <Paperclip class="w-4 h-4 flex-shrink-0 opacity-80" />
                        <span class="flex-1 min-w-0">
                            <span class="block text-xs font-medium truncate">{{ m.attachmentFileName }}</span>
                            <span :class="['block text-[10px]', isOwn(m) ? 'text-white/70' : 'text-slate-500']">{{ fmtSize(m.attachmentSizeBytes) }}</span>
                        </span>
                        <Download class="w-3.5 h-3.5 opacity-80" />
                    </a>
                </div>

                <footer v-if="isOwn(m)" class="text-[10px] text-slate-400 mt-0.5 px-1">
                    {{ readState(m) === 'read' ? '✓✓ leído' : '✓ enviado' }}
                </footer>
            </div>
        </article>

        <div v-if="typingNames.length > 0" class="text-xs text-slate-500 italic px-3 mt-1 flex items-center gap-2 self-start">
            <span class="flex gap-0.5">
                <span class="w-1.5 h-1.5 rounded-full bg-slate-400 animate-bounce" style="animation-delay:0ms;"></span>
                <span class="w-1.5 h-1.5 rounded-full bg-slate-400 animate-bounce" style="animation-delay:150ms;"></span>
                <span class="w-1.5 h-1.5 rounded-full bg-slate-400 animate-bounce" style="animation-delay:300ms;"></span>
            </span>
            {{ typingNames.join(", ") }} {{ typingNames.length === 1 ? "está" : "están" }} escribiendo…
        </div>
    </div>
</template>
