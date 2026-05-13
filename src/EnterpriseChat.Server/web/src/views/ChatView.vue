<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from "vue";
import { useRoute } from "vue-router";
import { useAuthStore } from "@/stores/auth";
import { useRoomsStore } from "@/stores/rooms";
import { useUsersStore } from "@/stores/users";
import { useMessagesStore } from "@/stores/messages";
import { useLicenseStore } from "@/stores/license";
import { chatHub } from "@/services/signalr";
import type { ChatMessage, ThreadKey } from "@/api/types";
import Sidebar from "@/components/Sidebar.vue";
import TopBar from "@/components/TopBar.vue";
import ChatWindow from "@/components/ChatWindow.vue";
import RightPanel from "@/components/RightPanel.vue";
import InboxView from "@/views/InboxView.vue";
import MentionsView from "@/views/MentionsView.vue";
import SavedView from "@/views/SavedView.vue";
import DraftsView from "@/views/DraftsView.vue";
import TeamView from "@/views/TeamView.vue";

const route = useRoute();
const auth = useAuthStore();
const rooms = useRoomsStore();
const users = useUsersStore();
const messages = useMessagesStore();
const license = useLicenseStore();

const connecting = ref(true);
const connectError = ref<string | null>(null);

const activeThread = computed<ThreadKey | null>(() => {
    if (route.name === "channel" && typeof route.params.roomId === "string") {
        return { kind: "room", roomId: parseInt(route.params.roomId, 10) };
    }
    if (route.name === "dm" && typeof route.params.peerUserId === "string") {
        return { kind: "dm", peerUserId: parseInt(route.params.peerUserId, 10) };
    }
    return null;
});

const centrePane = computed(() => route.name);

function handleMessageReceived(m: ChatMessage): void { messages.applyIncoming(m); }
function handlePresence(userId: number, isOnline: boolean): void { users.applyPresence(userId, isOnline); }
function handleMessageRead(serverId: number, byUserId: number): void { messages.applyMessageRead(serverId, byUserId); }
function handleTyping(fromUserId: number, toUserId: number | null, roomId: number | null): void { messages.applyTyping(fromUserId, toUserId, roomId); }
function handleLicenseDenied(reason: string): void { connectError.value = `Conexión rechazada por licencia: ${reason}`; }
function handleRoomMembership(_roomId: number, _userId: number, _joined: boolean): void { void rooms.load(); }

onMounted(async () => {
    if (auth.userId === null) return;
    messages.setCurrentUser(auth.userId);
    try {
        await chatHub.start();
        chatHub.on("OnMessageReceived", handleMessageReceived);
        chatHub.on("OnPresenceChanged", handlePresence);
        chatHub.on("OnMessageRead", handleMessageRead);
        chatHub.on("OnTyping", handleTyping);
        chatHub.on("OnLicenseDenied", handleLicenseDenied);
        chatHub.on("OnRoomMembershipChanged", handleRoomMembership);
        await Promise.all([rooms.load(), users.load(), license.load()]);
        connecting.value = false;
        if (activeThread.value !== null) {
            try { await messages.loadHistory(activeThread.value); } catch { /* ignore */ }
        }
    } catch (err) {
        const msg = err instanceof Error ? err.message : String(err);
        connectError.value = `No se pudo conectar al servidor: ${msg}`;
        connecting.value = false;
    }
});

onBeforeUnmount(async () => {
    chatHub.off("OnMessageReceived", handleMessageReceived);
    chatHub.off("OnPresenceChanged", handlePresence);
    chatHub.off("OnMessageRead", handleMessageRead);
    chatHub.off("OnTyping", handleTyping);
    chatHub.off("OnLicenseDenied", handleLicenseDenied);
    chatHub.off("OnRoomMembershipChanged", handleRoomMembership);
    await chatHub.stop();
});

watch(activeThread, async (key) => {
    if (key === null || connecting.value) return;
    try { await messages.loadHistory(key); } catch { /* ignore */ }
});
</script>

<template>
    <div class="h-screen grid grid-cols-[260px_minmax(0,1fr)_320px] grid-rows-[56px_1fr] bg-slate-50">
        <TopBar class="col-span-3 row-start-1 border-b border-slate-200" />
        <Sidebar class="row-start-2 border-r border-slate-200 overflow-hidden" />
        <main class="row-start-2 overflow-hidden flex flex-col">
            <div v-if="connecting" class="m-auto text-slate-500 text-sm">Conectando con el servidor…</div>
            <div v-else-if="connectError" class="m-auto max-w-md text-red-700 bg-red-50 border border-red-200 rounded-lg px-4 py-3 text-sm">
                {{ connectError }}
            </div>
            <template v-else>
                <ChatWindow v-if="activeThread !== null" :thread="activeThread" />
                <InboxView v-else-if="centrePane === 'inbox'" />
                <MentionsView v-else-if="centrePane === 'mentions'" />
                <SavedView v-else-if="centrePane === 'saved'" />
                <DraftsView v-else-if="centrePane === 'drafts'" />
                <TeamView v-else-if="centrePane === 'team'" />
                <div v-else class="m-auto text-slate-500 text-sm">Selecciona un canal o conversación.</div>
            </template>
        </main>
        <RightPanel class="row-start-2 border-l border-slate-200 overflow-hidden" :thread="activeThread" />
    </div>
</template>
