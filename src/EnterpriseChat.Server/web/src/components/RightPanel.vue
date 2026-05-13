<script setup lang="ts">
import { computed } from "vue";
import { useRoomsStore } from "@/stores/rooms";
import { useUsersStore } from "@/stores/users";
import { useLicenseStore } from "@/stores/license";
import type { ThreadKey } from "@/api/types";

const props = defineProps<{ thread: ThreadKey | null }>();
const rooms = useRoomsStore();
const users = useUsersStore();
const license = useLicenseStore();

const heading = computed(() => {
    if (props.thread === null) return "Detalles";
    if (props.thread.kind === "room") {
        const r = rooms.findById(props.thread.roomId);
        return r?.name ?? "Canal";
    }
    const u = users.findById(props.thread.peerUserId);
    return u?.fullName ?? "Usuario";
});
</script>

<template>
    <aside class="h-full overflow-y-auto bg-white px-4 py-4 flex flex-col gap-4">
        <header class="flex items-center justify-between">
            <strong class="text-slate-900">{{ heading }}</strong>
        </header>

        <section class="card p-4 bg-blue-50 border-blue-100">
            <h4 class="text-xs font-bold uppercase tracking-wider text-blue-800 mb-2">Servidor propio</h4>
            <ul class="text-xs text-slate-700 space-y-1.5">
                <li>🛡 Autoalojado en tu servidor</li>
                <li>🔒 Cifrado en tránsito (TLS)</li>
                <li>⚖ Código abierto AGPLv3</li>
            </ul>
        </section>

        <section v-if="license.info" class="card p-4">
            <h4 class="text-xs font-bold uppercase tracking-wider text-slate-500 mb-2">Licencia</h4>
            <dl class="text-xs text-slate-700 space-y-1">
                <div class="flex justify-between"><dt>Edición</dt><dd class="font-semibold">{{ license.info.edition }}</dd></div>
                <div class="flex justify-between"><dt>Máx. usuarios</dt><dd>{{ license.info.maxConcurrentUsers }}</dd></div>
                <div v-if="license.info.licensedTo" class="flex justify-between"><dt>Licenciada a</dt><dd>{{ license.info.licensedTo }}</dd></div>
                <div v-if="license.info.expiresAt" class="flex justify-between"><dt>Caduca</dt><dd>{{ new Date(license.info.expiresAt).toLocaleDateString('es-ES') }}</dd></div>
            </dl>
        </section>
    </aside>
</template>
