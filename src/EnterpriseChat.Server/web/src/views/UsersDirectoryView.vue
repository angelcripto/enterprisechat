<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { Users, Search, MessageCircle, Circle } from "lucide-vue-next";
import Avatar from "@/components/Avatar.vue";
import { useAuthStore } from "@/stores/auth";
import { useUsersStore } from "@/stores/users";

/**
 * Directorio de usuarios — vista de solo lectura accesible para
 * cualquier rol. Lista todas las cuentas activas y permite abrir un
 * DM con un click. Cuando llegue PR de "directorio rico" se podrán
 * añadir departamento/cargo/teléfono como filtros, pero por ahora
 * basta con nombre + estado + búsqueda.
 */
const auth = useAuthStore();
const users = useUsersStore();
const router = useRouter();

const query = ref("");

const filtered = computed(() => {
    const q = query.value.trim().toLowerCase();
    const base = users.users.filter((u) => u.id !== auth.userId);
    if (!q) return base;
    return base.filter((u) =>
        u.fullName.toLowerCase().includes(q)
        || u.username.toLowerCase().includes(q)
        || (u.department ?? "").toLowerCase().includes(q));
});

onMounted(async () => {
    if (users.users.length === 0) {
        try { await users.load(); } catch { /* ignore */ }
    }
});

function openDm(userId: number): void {
    void router.push({ name: "dm", params: { peerUserId: String(userId) } });
}
</script>

<template>
    <div class="h-full overflow-y-auto bg-slate-50 px-6 py-8">
        <div class="max-w-4xl mx-auto">
            <header class="mb-6">
                <h1 class="text-2xl font-bold text-slate-900 flex items-center gap-2">
                    <Users class="w-6 h-6 text-blue-600" />
                    Directorio
                </h1>
                <p class="text-sm text-slate-600 mt-1">Personas con las que puedes hablar en este servidor.</p>
            </header>

            <div class="card p-3 bg-white mb-4">
                <label class="flex items-center gap-2">
                    <Search class="w-4 h-4 text-slate-400" />
                    <input v-model="query" type="text" class="input flex-1 border-0 focus:ring-0" placeholder="Buscar por nombre, username o departamento…" />
                </label>
            </div>

            <ul class="card divide-y divide-slate-100 bg-white overflow-hidden">
                <li v-if="filtered.length === 0" class="px-4 py-6 text-sm text-slate-500 text-center">
                    No hay coincidencias.
                </li>
                <li v-for="u in filtered" :key="u.id" class="px-4 py-3 flex items-center gap-3 hover:bg-slate-50 cursor-pointer" @click="openDm(u.id)">
                    <Avatar :user-id="u.id" :full-name="u.fullName" :has-avatar="u.hasAvatar" :size="40" :show-status="true" :online="u.isOnline" />
                    <div class="flex-1 min-w-0">
                        <div class="text-sm font-medium text-slate-900 truncate">{{ u.fullName }}</div>
                        <div class="text-xs text-slate-500 flex items-center gap-2">
                            <span>@{{ u.username }}</span>
                            <span v-if="u.department">· {{ u.department }}</span>
                            <span class="flex items-center gap-1">
                                <Circle :class="['w-2 h-2', u.isOnline ? 'fill-emerald-500 text-emerald-500' : 'fill-slate-300 text-slate-300']" />
                                {{ u.isOnline ? "Online" : "Offline" }}
                            </span>
                        </div>
                    </div>
                    <button type="button" class="btn btn-secondary text-xs" @click.stop="openDm(u.id)">
                        <MessageCircle class="w-3.5 h-3.5" />
                        Escribir
                    </button>
                </li>
            </ul>
        </div>
    </div>
</template>
