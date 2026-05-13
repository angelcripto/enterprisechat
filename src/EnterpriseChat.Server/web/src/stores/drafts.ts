import { defineStore } from "pinia";
import { computed, reactive } from "vue";
import type { ThreadKey } from "@/api/types";
import { threadKeyId } from "@/api/types";

/**
 * Borradores: vive 100% en localStorage. Cada thread (canal o DM) puede
 * tener un borrador del mensaje que el usuario empezó a escribir y dejó
 * a medias. Cuando vuelve, se rellena automáticamente el input.
 */
const STORAGE_KEY = "ecat.drafts.v1";

interface Draft {
    body: string;
    updatedAt: number;
}

function readAll(): Record<string, Draft> {
    try {
        const raw = localStorage.getItem(STORAGE_KEY);
        if (raw === null) return {};
        return JSON.parse(raw) as Record<string, Draft>;
    } catch {
        return {};
    }
}

function writeAll(map: Record<string, Draft>): void {
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(map)); } catch { /* ignore */ }
}

export const useDraftsStore = defineStore("drafts", () => {
    const map = reactive<Record<string, Draft>>(readAll());

    const count = computed(() => {
        return Object.values(map).filter((d) => d.body.trim() !== "").length;
    });

    function get(key: ThreadKey): string {
        return map[threadKeyId(key)]?.body ?? "";
    }

    function set(key: ThreadKey, body: string): void {
        const id = threadKeyId(key);
        if (body.trim() === "") {
            delete map[id];
        } else {
            map[id] = { body, updatedAt: Date.now() };
        }
        writeAll({ ...map });
    }

    function clear(key: ThreadKey): void {
        delete map[threadKeyId(key)];
        writeAll({ ...map });
    }

    function entries(): Array<{ key: ThreadKey; body: string; updatedAt: number }> {
        return Object.entries(map).map(([id, draft]) => {
            const [kind, num] = id.split(":");
            const key: ThreadKey = kind === "room"
                ? { kind: "room", roomId: parseInt(num, 10) }
                : { kind: "dm", peerUserId: parseInt(num, 10) };
            return { key, body: draft.body, updatedAt: draft.updatedAt };
        }).sort((a, b) => b.updatedAt - a.updatedAt);
    }

    return { count, get, set, clear, entries };
});
