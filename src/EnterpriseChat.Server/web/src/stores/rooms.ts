import { defineStore } from "pinia";
import { computed, ref } from "vue";
import { api } from "@/api/client";
import { chatHub } from "@/services/signalr";
import type { RoomSummary } from "@/api/types";

export const useRoomsStore = defineStore("rooms", () => {
    const rooms = ref<RoomSummary[]>([]);
    const loading = ref(false);

    const memberRooms = computed(() => rooms.value.filter((r) => r.isMember));
    const discoverableRooms = computed(() => rooms.value.filter((r) => !r.isMember));

    async function load(): Promise<void> {
        loading.value = true;
        try {
            const { data } = await api.get<RoomSummary[]>("/rooms");
            rooms.value = data;
        } finally {
            loading.value = false;
        }
    }

    async function create(name: string, isPrivate: boolean): Promise<number> {
        const id = await chatHub.createRoom(name, isPrivate);
        await load();
        return id;
    }

    async function join(roomId: number): Promise<void> {
        await chatHub.joinRoom(roomId);
        await load();
    }

    async function leave(roomId: number): Promise<void> {
        await chatHub.leaveRoom(roomId);
        await load();
    }

    function findById(id: number): RoomSummary | undefined {
        return rooms.value.find((r) => r.id === id);
    }

    return { rooms, loading, memberRooms, discoverableRooms, load, create, join, leave, findById };
});
