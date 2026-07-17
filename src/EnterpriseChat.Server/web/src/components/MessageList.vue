<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from "vue";
import { Paperclip, Download, SmilePlus, Pin, Bookmark, MoreHorizontal, ArrowDown } from "lucide-vue-next";
import EmojiPicker from "vue3-emoji-picker";
import "vue3-emoji-picker/css";
import { useAuthStore } from "@/stores/auth";
import { useUsersStore } from "@/stores/users";
import { useMessagesStore } from "@/stores/messages";
import { useReactionsStore } from "@/stores/reactions";
import { usePinnedStore } from "@/stores/pinned";
import { useSavedStore } from "@/stores/saved";
import { chatHub } from "@/services/signalr";
import { getAccessToken } from "@/api/client";
import Avatar from "@/components/Avatar.vue";
import type { ChatMessage, ThreadKey, ReactionSummary } from "@/api/types";
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
const reactionsStore = useReactionsStore();
const pinned = usePinnedStore();
const saved = useSavedStore();

const scrollEl = ref<HTMLDivElement | null>(null);
const items = computed(() => messages.get(props.thread));

/** Han llegado mensajes mientras leías más arriba: muestra el botón de bajar. */
const hasNewBelow = ref(false);

const typingNames = computed(() => {
    const thread = messages.typingByThread[threadKeyId(props.thread)];
    if (thread === undefined) return [];
    return Object.keys(thread)
        .map(Number)
        .filter((id) => id !== auth.userId)
        .map((id) => users.findById(id)?.fullName ?? "Alguien")
        .slice(0, 3);
});

const IMAGE_EXTS = new Set(["png", "jpg", "jpeg", "gif", "webp", "bmp", "avif", "svg"]);

const pickerOpenFor = ref<string | null>(null);

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

function messageId(m: ChatMessage): number | null {
    return m.serverId ?? null;
}

function reactionsFor(m: ChatMessage): ReactionSummary[] {
    const id = messageId(m);
    return id === null ? [] : reactionsStore.get(id);
}

function isPinned(m: ChatMessage): boolean {
    if (props.thread.kind !== "room") return false;
    const id = messageId(m);
    if (id === null) return false;
    return pinned.get(props.thread.roomId).some((p) => p.messageId === id);
}

async function toggleReaction(m: ChatMessage, emoji: string): Promise<void> {
    const id = messageId(m);
    if (id === null) return;
    try { await reactionsStore.toggle(id, emoji); } catch { /* ignore */ }
}

function onEmojiPick(m: ChatMessage, payload: { i: string }): void {
    void toggleReaction(m, payload.i);
    pickerOpenFor.value = null;
}

async function togglePin(m: ChatMessage): Promise<void> {
    if (props.thread.kind !== "room") return;
    const id = messageId(m);
    if (id === null) return;
    try {
        if (isPinned(m)) {
            await pinned.unpin(props.thread.roomId, id);
        } else {
            await pinned.pin(props.thread.roomId, id);
        }
    } catch { /* ignore */ }
}

async function toggleSave(m: ChatMessage): Promise<void> {
    const id = messageId(m);
    if (id === null) return;
    try { await saved.toggle(id); } catch { /* ignore */ }
}

// Load reactions for every visible message whose serverId is known. Coalesced
// to avoid hammering the API when history bulk-loads.
let reactionsLoadTimer = 0;
function scheduleReactionsLoad(): void {
    if (reactionsLoadTimer !== 0) return;
    reactionsLoadTimer = window.setTimeout(() => {
        reactionsLoadTimer = 0;
        for (const m of items.value) {
            const id = messageId(m);
            if (id !== null && !(id in reactionsStore.byMessage)) {
                void reactionsStore.load(id);
            }
        }
    }, 100);
}

onMounted(() => {
    document.addEventListener("click", onDocumentClick);
});

function onDocumentClick(e: MouseEvent): void {
    if (pickerOpenFor.value === null) return;
    const target = e.target as HTMLElement;
    if (!target.closest?.("[data-emoji-popover]") && !target.closest?.("[data-emoji-trigger]")) {
        pickerOpenFor.value = null;
    }
}

/** Margen para considerar que el usuario "está abajo". Un par de píxeles de
 *  diferencia por redondeo de zoom no deben contar como "se ha ido a leer". */
const NEAR_BOTTOM_PX = 80;

function isNearBottom(el: HTMLElement): boolean {
    return el.scrollHeight - el.scrollTop - el.clientHeight <= NEAR_BOTTOM_PX;
}

function scrollToBottom(smooth = false): void {
    const el = scrollEl.value;
    if (el === null) return;
    el.scrollTo({ top: el.scrollHeight, behavior: smooth ? "smooth" : "auto" });
    hasNewBelow.value = false;
}

/** Si el usuario baja del todo a mano, el aviso de mensajes nuevos sobra. */
function onScroll(): void {
    const el = scrollEl.value;
    if (el !== null && isNearBottom(el)) {
        hasNewBelow.value = false;
    }
}

// Se lleva la cuenta a mano en vez de comparar (next, prev): con `deep: true`
// ambos apuntan al MISMO array (applyIncoming hace push sobre el existente),
// así que `next.length > prev.length` nunca sería cierto.
let lastCount = 0;

watch(items, async () => {
    // flush "pre": el DOM todavía NO tiene el mensaje nuevo, así que esto mide
    // dónde estaba el usuario ANTES de que la lista creciera. Con "post" ya sería
    // tarde: el scroll habría cambiado de sitio y todo parecería "no estaba abajo".
    const el = scrollEl.value;
    const wasAtBottom = el === null || isNearBottom(el);

    const count = items.value.length;
    const grew = count > lastCount;
    lastCount = count;

    const last = items.value[count - 1];
    const lastIsMine = last !== undefined && last.fromUserId === auth.userId;

    await nextTick();

    // Bajar sólo si ya estabas abajo, o si el mensaje nuevo es tuyo (acabas de
    // enviarlo: siempre quieres verlo). Si estabas leyendo historial más arriba,
    // no te movemos: te avisamos con el botón.
    if (wasAtBottom || (grew && lastIsMine)) {
        scrollToBottom();
    } else if (grew) {
        hasNewBelow.value = true;
    }

    if (last !== undefined && last.serverId !== null && last.serverId !== undefined && last.fromUserId !== auth.userId) {
        try { await chatHub.markAsRead(last.serverId); } catch { /* ignore */ }
    }
    scheduleReactionsLoad();
}, { deep: true, flush: "pre", immediate: true });

// El aviso de "escribiendo…" también cambia la altura de la lista. Si estabas
// abajo, hay que seguir abajo cuando aparece o desaparece.
watch(typingNames, async () => {
    const el = scrollEl.value;
    if (el === null || !isNearBottom(el)) return;
    await nextTick();
    scrollToBottom();
});

// Al cambiar de conversación se entra siempre por el final, como en WhatsApp.
watch(() => props.thread, async () => {
    hasNewBelow.value = false;
    lastCount = 0;
    await nextTick();
    scrollToBottom();
});
</script>

<template>
    <!--
      Layout tipo WhatsApp: la conversación reposa sobre el cuadro de escribir y
      crece hacia arriba. El scroll vive en el contenedor exterior; el interior
      lleva `min-h-full` + `justify-end` para empujar el contenido al fondo
      cuando hay pocos mensajes.

      Ojo: `justify-end` va en el envoltorio INTERIOR, nunca en el contenedor con
      scroll — ahí deja los mensajes de arriba inalcanzables en Chrome y Firefox.
      El padding también va dentro: con box-sizing:border-box entra en el 100% de
      `min-h-full`, si no aparecería una barra de scroll fantasma con la lista vacía.
    -->
    <div ref="scrollEl" class="relative h-full min-h-0 overflow-y-auto" @scroll.passive="onScroll">
      <div class="flex flex-col gap-2 min-h-full justify-end px-6 py-4">
        <div v-if="items.length === 0" class="m-auto text-slate-400 text-sm">
            Sin mensajes todavía. Escribe algo abajo para iniciar la conversación.
        </div>

        <article
            v-for="m in items"
            :key="m.messageId"
            :class="['group relative flex items-end gap-2 max-w-full', isOwn(m) ? 'flex-row-reverse self-end' : 'self-start']"
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
                    <Pin v-if="isPinned(m)" class="w-3 h-3 text-amber-500" />
                </header>

                <!-- Image bubble -->
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

                <!-- Regular bubble -->
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

                <!-- Reaction chips -->
                <div v-if="reactionsFor(m).length > 0" class="flex flex-wrap gap-1 mt-1 px-1" :class="isOwn(m) ? 'justify-end' : 'justify-start'">
                    <button
                        v-for="r in reactionsFor(m)"
                        :key="r.emoji"
                        type="button"
                        class="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs border transition-colors"
                        :class="r.mine
                            ? 'bg-blue-50 border-blue-300 text-blue-800 hover:bg-blue-100'
                            : 'bg-slate-100 border-slate-200 text-slate-700 hover:bg-slate-200'"
                        :title="r.mine ? 'Quitar tu reacción' : 'Sumar tu reacción'"
                        @click.stop="toggleReaction(m, r.emoji)"
                    >
                        <span>{{ r.emoji }}</span>
                        <span class="font-mono text-[10px]">{{ r.count }}</span>
                    </button>
                </div>

                <footer v-if="isOwn(m)" class="text-[10px] text-slate-400 mt-0.5 px-1">
                    {{ readState(m) === 'read' ? '✓✓ leído' : '✓ enviado' }}
                </footer>
            </div>

            <!-- Hover toolbar -->
            <div
                v-if="messageId(m) !== null"
                class="absolute top-1 opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none group-hover:pointer-events-auto"
                :class="isOwn(m) ? 'left-2' : 'right-2'"
            >
                <div class="flex items-center gap-0.5 bg-white border border-slate-200 rounded-full shadow-md px-1 py-0.5">
                    <button
                        type="button"
                        data-emoji-trigger
                        class="p-1.5 rounded-full hover:bg-slate-100 text-slate-600"
                        title="Añadir reacción"
                        @click.stop="pickerOpenFor = pickerOpenFor === m.messageId ? null : m.messageId"
                    >
                        <SmilePlus class="w-3.5 h-3.5" />
                    </button>
                    <button
                        v-if="thread.kind === 'room'"
                        type="button"
                        class="p-1.5 rounded-full hover:bg-slate-100 text-slate-600"
                        :class="{ 'text-amber-600': isPinned(m) }"
                        :title="isPinned(m) ? 'Desfijar' : 'Fijar en el canal'"
                        @click.stop="togglePin(m)"
                    >
                        <Pin class="w-3.5 h-3.5" />
                    </button>
                    <button
                        type="button"
                        class="p-1.5 rounded-full hover:bg-slate-100 text-slate-600"
                        title="Guardar para más tarde"
                        @click.stop="toggleSave(m)"
                    >
                        <Bookmark class="w-3.5 h-3.5" />
                    </button>
                    <button
                        type="button"
                        class="p-1.5 rounded-full hover:bg-slate-100 text-slate-600"
                        title="Más opciones"
                    >
                        <MoreHorizontal class="w-3.5 h-3.5" />
                    </button>
                </div>
            </div>

            <!-- Emoji picker popover -->
            <div
                v-if="pickerOpenFor === m.messageId"
                data-emoji-popover
                class="absolute z-30 top-10"
                :class="isOwn(m) ? 'left-2' : 'right-2'"
                @click.stop
            >
                <EmojiPicker
                    :native="true"
                    :hide-search="false"
                    :disable-skin-tones="true"
                    @select="(p) => onEmojiPick(m, p)"
                />
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

      <!-- Aviso de mensajes nuevos cuando llegan y estás leyendo más arriba.
           `sticky bottom-4` lo ancla al viewport del scroll sin sacarlo del
           flujo, así no tapa el último mensaje ni necesita que el contenedor
           sea `relative`. -->
      <Transition name="fade">
        <div v-if="hasNewBelow" class="sticky bottom-4 flex justify-center pointer-events-none">
          <button
            type="button"
            class="pointer-events-auto flex items-center gap-1.5 rounded-full bg-blue-600 text-white text-xs font-medium px-3 py-1.5 shadow-lg hover:bg-blue-700"
            @click="scrollToBottom(true)"
          >
            <ArrowDown class="w-3.5 h-3.5" />
            Mensajes nuevos
          </button>
        </div>
      </Transition>
    </div>
</template>

<style scoped>
.fade-enter-active,
.fade-leave-active {
    transition: opacity 0.15s ease;
}

.fade-enter-from,
.fade-leave-to {
    opacity: 0;
}
</style>
