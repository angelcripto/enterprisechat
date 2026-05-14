<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { Pin, ChevronDown, X } from "lucide-vue-next";
import { usePinnedStore } from "@/stores/pinned";
import { useUsersStore } from "@/stores/users";
import type { PinnedSummary } from "@/api/types";

const props = defineProps<{ roomId: number }>();

const pinned = usePinnedStore();
const users = useUsersStore();

const open = ref(false);
const items = computed<PinnedSummary[]>(() => pinned.get(props.roomId));

async function refresh(): Promise<void> {
    try { await pinned.loadForRoom(props.roomId); } catch { /* ignore */ }
}

onMounted(refresh);
watch(() => props.roomId, refresh);

function authorName(uid: number): string {
    return users.findById(uid)?.fullName ?? "Usuario";
}

function preview(body: string): string {
    const trimmed = body.trim().replace(/\s+/g, " ");
    return trimmed.length > 160 ? trimmed.slice(0, 157) + "…" : trimmed;
}

function date(s: string): string {
    return new Date(s).toLocaleDateString("es-ES", { day: "numeric", month: "short" });
}

async function unpin(messageId: number): Promise<void> {
    try { await pinned.unpin(props.roomId, messageId); } catch { /* ignore */ }
}
</script>

<template>
    <div v-if="items.length > 0" class="border-b border-amber-200 bg-amber-50 text-amber-900">
        <button
            type="button"
            class="w-full px-4 py-2 flex items-center gap-2 text-xs font-medium hover:bg-amber-100 transition-colors"
            @click="open = !open"
        >
            <Pin class="w-3.5 h-3.5" />
            <span>{{ items.length }} {{ items.length === 1 ? 'mensaje fijado' : 'mensajes fijados' }}</span>
            <ChevronDown class="w-3.5 h-3.5 ml-auto transition-transform" :class="{ 'rotate-180': open }" />
        </button>
        <ul v-if="open" class="divide-y divide-amber-100 max-h-72 overflow-y-auto">
            <li
                v-for="p in items"
                :key="p.messageId"
                class="px-4 py-2.5 text-sm flex items-start gap-3 hover:bg-amber-100/60"
            >
                <div class="flex-1 min-w-0">
                    <header class="flex items-baseline gap-2 mb-0.5">
                        <strong class="text-xs text-amber-900">{{ authorName(p.authorUserId) }}</strong>
                        <span class="text-[10px] text-amber-700/70">{{ date(p.sentAt) }}</span>
                    </header>
                    <p class="text-xs text-amber-950 leading-snug m-0">{{ preview(p.body) }}</p>
                </div>
                <button
                    type="button"
                    class="text-amber-700 hover:text-amber-900 p-1 -m-1"
                    title="Desfijar"
                    @click="unpin(p.messageId)"
                >
                    <X class="w-3.5 h-3.5" />
                </button>
            </li>
        </ul>
    </div>
</template>
