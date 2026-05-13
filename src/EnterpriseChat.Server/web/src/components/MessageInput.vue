<script setup lang="ts">
import { ref, watch } from "vue";
import { Paperclip, Smile, Send } from "lucide-vue-next";
import { useMessagesStore } from "@/stores/messages";
import { useDraftsStore } from "@/stores/drafts";
import { chatHub } from "@/services/signalr";
import { api } from "@/api/client";
import type { AttachmentSummary, ThreadKey } from "@/api/types";

const props = defineProps<{ thread: ThreadKey }>();
const messages = useMessagesStore();
const drafts = useDraftsStore();

const body = ref(drafts.get(props.thread));
const sending = ref(false);
const error = ref<string | null>(null);
const fileInput = ref<HTMLInputElement | null>(null);
let typingThrottle = 0;

// Switch threads → restore the draft for the new thread.
watch(() => props.thread, (t) => {
    body.value = drafts.get(t);
});

// Auto-save draft as the user types (debounced de facto by the v-model + watcher).
watch(body, (newBody) => {
    drafts.set(props.thread, newBody);
});

async function send(): Promise<void> {
    if (sending.value) return;
    const text = body.value.trim();
    if (text === "") return;
    sending.value = true;
    error.value = null;
    try {
        await messages.sendText(props.thread, text);
        body.value = "";
        drafts.clear(props.thread);
    } catch (err) {
        error.value = err instanceof Error ? err.message : "No se pudo enviar.";
    } finally {
        sending.value = false;
    }
}

function onKeyDown(e: KeyboardEvent): void {
    if (e.key === "Enter" && !e.shiftKey) {
        e.preventDefault();
        void send();
    } else {
        notifyTyping();
    }
}

function notifyTyping(): void {
    const now = Date.now();
    if (now - typingThrottle < 2000) return;
    typingThrottle = now;
    const toUserId = props.thread.kind === "dm" ? props.thread.peerUserId : null;
    const roomId = props.thread.kind === "room" ? props.thread.roomId : null;
    void chatHub.typing(toUserId, roomId);
}

function openFilePicker(): void {
    fileInput.value?.click();
}

async function onFileSelected(ev: Event): Promise<void> {
    const target = ev.target as HTMLInputElement;
    const file = target.files?.[0];
    target.value = "";
    if (file === undefined) return;
    sending.value = true;
    error.value = null;
    try {
        const fd = new FormData();
        fd.append("file", file);
        const { data } = await api.post<AttachmentSummary>("/files", fd);
        const text = body.value.trim();
        if (props.thread.kind === "room") {
            await chatHub.sendRoomMessageWithAttachment(props.thread.roomId, text, data.id);
        } else {
            await chatHub.sendDirectMessageWithAttachment(props.thread.peerUserId, text, data.id);
        }
        body.value = "";
        drafts.clear(props.thread);
    } catch (err) {
        error.value = err instanceof Error ? err.message : "No se pudo subir el archivo.";
    } finally {
        sending.value = false;
    }
}
</script>

<template>
    <div class="border-t border-slate-200 px-4 py-3 bg-slate-50">
        <p v-if="error" class="text-xs text-red-700 bg-red-50 border border-red-200 rounded px-3 py-1.5 mb-2">{{ error }}</p>
        <div class="flex items-end gap-2 bg-white border border-slate-200 rounded-xl px-3 py-2 focus-within:border-blue-500 focus-within:ring-3 focus-within:ring-blue-100">
            <button type="button" class="text-slate-500 hover:text-slate-700 p-1" @click="openFilePicker" aria-label="Adjuntar archivo">
                <Paperclip class="w-4 h-4" />
            </button>
            <input ref="fileInput" type="file" class="hidden" @change="onFileSelected" />
            <textarea
                v-model="body"
                rows="1"
                placeholder="Escribe un mensaje…"
                class="flex-1 resize-none bg-transparent text-sm text-slate-900 placeholder:text-slate-400 focus:outline-none max-h-40"
                @keydown="onKeyDown"
            ></textarea>
            <button type="button" class="text-slate-400 p-1 opacity-50" aria-label="Emoji (próximamente)" disabled>
                <Smile class="w-4 h-4" />
            </button>
            <button type="button" class="btn btn-primary text-sm" :disabled="sending || body.trim() === ''" @click="send">
                <Send class="w-4 h-4" />
                Enviar
            </button>
        </div>
    </div>
</template>
