<script setup lang="ts">
import { computed } from "vue";
import { useRoute, useRouter } from "vue-router";
import { Briefcase } from "lucide-vue-next";
import Avatar from "@/components/Avatar.vue";
import { useUsersStore } from "@/stores/users";

const route = useRoute();
const router = useRouter();
const users = useUsersStore();

const teamName = computed(() => (route.params.name as string) ?? "");
const members = computed(() => users.users.filter((u) => u.department === teamName.value));

function goDm(userId: number): void {
    void router.push({ name: "dm", params: { peerUserId: String(userId) } });
}
</script>

<template>
    <div class="h-full overflow-y-auto bg-slate-50">
        <header class="sticky top-0 bg-white border-b border-slate-200 px-6 py-3 flex items-center gap-2">
            <Briefcase class="w-5 h-5 text-slate-500" />
            <h1 class="text-base font-semibold text-slate-900">Equipo · {{ teamName }}</h1>
            <span class="ml-2 text-xs text-slate-500">{{ members.length }} miembros</span>
        </header>
        <div class="max-w-3xl mx-auto p-6">
            <ul v-if="members.length > 0" class="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <li v-for="u in members" :key="u.id">
                    <button type="button" @click="goDm(u.id)" class="w-full flex items-center gap-3 p-3 bg-white rounded-xl border border-slate-200 hover:border-blue-300 text-left">
                        <Avatar :user-id="u.id" :full-name="u.fullName" :has-avatar="u.hasAvatar" :size="40" :show-status="true" :online="u.isOnline" />
                        <span class="flex-1 min-w-0">
                            <span class="block text-sm font-semibold text-slate-900 truncate">{{ u.fullName }}</span>
                            <span class="block text-xs text-slate-500 truncate">@{{ u.username }}</span>
                        </span>
                        <span class="text-[10px] uppercase tracking-wider font-semibold" :class="u.role === 'Admin' ? 'text-blue-600' : 'text-slate-400'">
                            {{ u.role }}
                        </span>
                    </button>
                </li>
            </ul>
            <div v-else class="bg-white rounded-xl border border-slate-200 p-10 text-center text-sm text-slate-500">
                No hay miembros en este equipo.
            </div>
        </div>
    </div>
</template>
