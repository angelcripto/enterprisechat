<script setup lang="ts">
import { computed, watch } from "vue";
import { FileText, Users as UsersIcon, ShieldCheck, Server, Lock, BarChart3, Hash, AtSign, Mail, Briefcase, BellOff, LogOut as Leave } from "lucide-vue-next";
import Avatar from "@/components/Avatar.vue";
import { useRoomsStore } from "@/stores/rooms";
import { useUsersStore } from "@/stores/users";
import { useLicenseStore } from "@/stores/license";
import { useFilesStore } from "@/stores/files";
import { useMetricsStore } from "@/stores/metrics";
import { getAccessToken } from "@/api/client";
import { isProEdition } from "@/api/types";
import type { ThreadKey } from "@/api/types";

const props = defineProps<{ thread: ThreadKey | null }>();
const rooms = useRoomsStore();
const users = useUsersStore();
const license = useLicenseStore();
const files = useFilesStore();
const metrics = useMetricsStore();

function fileUrl(id: number): string {
    const token = getAccessToken();
    return token === null ? `/files/${id}` : `/files/${id}?access_token=${encodeURIComponent(token)}`;
}

const activeRoom = computed(() => {
    if (props.thread?.kind !== "room") return null;
    return rooms.findById(props.thread.roomId) ?? null;
});

const peer = computed(() => {
    if (props.thread?.kind !== "dm") return null;
    return users.findById(props.thread.peerUserId) ?? null;
});

const roomFiles = computed(() => {
    if (props.thread?.kind !== "room") return [];
    return files.get(props.thread.roomId).slice(0, 6);
});

const memberSample = computed(() => users.users.slice(0, 6));

function fmtBytes(b: number): string {
    if (b < 1024) return `${b} B`;
    if (b < 1024 * 1024) return `${(b / 1024).toFixed(1)} KB`;
    if (b < 1024 * 1024 * 1024) return `${(b / 1024 / 1024).toFixed(1)} MB`;
    return `${(b / 1024 / 1024 / 1024).toFixed(2)} GB`;
}

function pct(): number {
    if (metrics.info === null || metrics.info.storageQuotaBytes === 0) return 0;
    return Math.min(100, Math.round((metrics.info.storageUsedBytes / metrics.info.storageQuotaBytes) * 100));
}

watch(() => props.thread, async (t) => {
    if (t?.kind === "room") {
        try { await files.loadForRoom(t.roomId); } catch { /* ignore */ }
    }
    try { await metrics.load(); } catch { /* ignore */ }
}, { immediate: true });
</script>

<template>
    <aside class="h-full overflow-y-auto bg-white">
        <header class="flex items-center justify-between px-4 py-3 border-b border-slate-100 sticky top-0 bg-white z-10">
            <strong class="text-slate-900">
                {{ thread === null ? 'Información' : (thread.kind === 'room' ? 'Detalles del canal' : 'Perfil') }}
            </strong>
        </header>

        <div class="px-4 py-4 flex flex-col gap-5">
            <!-- ============== DM mode ============== -->
            <template v-if="thread?.kind === 'dm' && peer !== null">
                <section class="flex flex-col items-center gap-2 text-center">
                    <Avatar :user-id="peer.id" :full-name="peer.fullName" :has-avatar="peer.hasAvatar" :size="80" />
                    <strong class="text-base text-slate-900 mt-2">{{ peer.fullName }}</strong>
                    <span class="text-xs text-slate-500">@{{ peer.username }}</span>
                    <span class="inline-flex items-center gap-1.5 text-xs mt-1">
                        <span class="w-2 h-2 rounded-full" :class="peer.isOnline ? 'bg-emerald-500' : 'bg-slate-300'"></span>
                        <span :class="peer.isOnline ? 'text-emerald-700' : 'text-slate-500'">
                            {{ peer.isOnline ? 'En línea' : 'Desconectado' }}
                        </span>
                    </span>
                </section>

                <section class="rounded-xl border border-slate-200 p-3 text-sm">
                    <h3 class="text-xs font-bold uppercase tracking-wider text-slate-500 mb-2">Acerca de</h3>
                    <dl class="space-y-1.5 text-xs">
                        <div class="flex items-start gap-2">
                            <Briefcase class="w-3.5 h-3.5 text-slate-400 mt-0.5" />
                            <span class="text-slate-700">{{ peer.department ?? 'Sin departamento' }}</span>
                        </div>
                        <div class="flex items-start gap-2">
                            <AtSign class="w-3.5 h-3.5 text-slate-400 mt-0.5" />
                            <span class="text-slate-700">{{ peer.username }}</span>
                        </div>
                        <div class="flex items-start gap-2">
                            <Mail class="w-3.5 h-3.5 text-slate-400 mt-0.5" />
                            <span class="text-slate-700">{{ peer.role === 'Admin' ? 'Administrador' : 'Usuario' }}</span>
                        </div>
                    </dl>
                </section>

                <section class="flex flex-col gap-1.5">
                    <button type="button" class="flex items-center gap-2 px-3 py-2 rounded-lg hover:bg-slate-50 text-sm text-slate-700">
                        <BellOff class="w-4 h-4 text-slate-500" />
                        Silenciar notificaciones
                    </button>
                </section>
            </template>

            <!-- ============== Channel mode ============== -->
            <template v-else-if="thread?.kind === 'room' && activeRoom !== null">
                <section>
                    <div class="flex items-center gap-2 mb-1">
                        <component :is="activeRoom.isPrivate ? Lock : Hash" class="w-5 h-5 text-slate-500" />
                        <strong class="text-base text-slate-900">{{ activeRoom.name }}</strong>
                    </div>
                    <p class="text-xs text-slate-500">
                        {{ activeRoom.memberCount }} miembros · {{ activeRoom.isPrivate ? 'Privado' : 'Público' }} · Creado {{ new Date(activeRoom.createdAt).toLocaleDateString('es-ES') }}
                    </p>
                </section>

                <section>
                    <header class="flex items-center justify-between mb-2">
                        <h3 class="text-sm font-semibold text-slate-900 flex items-center gap-1.5">
                            <UsersIcon class="w-4 h-4 text-slate-500" />
                            Miembros ({{ users.users.length }})
                        </h3>
                    </header>
                    <div class="flex items-center gap-1.5 flex-wrap">
                        <Avatar
                            v-for="u in memberSample"
                            :key="u.id"
                            :user-id="u.id"
                            :full-name="u.fullName"
                            :has-avatar="u.hasAvatar"
                            :show-status="true"
                            :online="u.isOnline"
                            :size="34"
                        />
                        <span v-if="users.users.length > memberSample.length" class="w-9 h-9 rounded-full bg-slate-100 grid place-items-center text-xs font-semibold text-slate-600">
                            +{{ users.users.length - memberSample.length }}
                        </span>
                    </div>
                </section>

                <section>
                    <header class="flex items-center justify-between mb-2">
                        <h3 class="text-sm font-semibold text-slate-900 flex items-center gap-1.5">
                            <FileText class="w-4 h-4 text-slate-500" />
                            Archivos compartidos
                        </h3>
                    </header>
                    <ul v-if="roomFiles.length > 0" class="flex flex-col gap-2">
                        <li v-for="f in roomFiles" :key="f.id">
                            <a :href="fileUrl(f.id)" target="_blank" rel="noopener" class="flex items-center gap-2.5 px-2 py-1.5 hover:bg-slate-50 rounded-md">
                                <span class="w-8 h-8 rounded-md bg-rose-50 text-rose-600 grid place-items-center text-[10px] font-bold uppercase">
                                    {{ f.fileName.split('.').pop()?.slice(0, 3) || 'FILE' }}
                                </span>
                                <span class="flex-1 min-w-0">
                                    <span class="block text-sm text-slate-900 truncate font-medium">{{ f.fileName }}</span>
                                    <span class="block text-[11px] text-slate-500">{{ fmtBytes(f.sizeBytes) }}</span>
                                </span>
                            </a>
                        </li>
                    </ul>
                    <p v-else class="text-xs text-slate-400 px-2">Aún no hay archivos en este canal.</p>
                </section>

                <section class="flex flex-col gap-1.5">
                    <button type="button" class="flex items-center gap-2 px-3 py-2 rounded-lg hover:bg-slate-50 text-sm text-slate-700">
                        <BellOff class="w-4 h-4 text-slate-500" />
                        Silenciar canal
                    </button>
                    <button type="button" class="flex items-center gap-2 px-3 py-2 rounded-lg hover:bg-red-50 text-sm text-red-600">
                        <Leave class="w-4 h-4" />
                        Abandonar canal
                    </button>
                </section>
            </template>

            <!-- ============== No thread (Inbox / Mentions / Saved / Drafts / Teams) ============== -->
            <template v-else>
                <p class="text-xs text-slate-500">Selecciona una conversación para ver detalles aquí.</p>
            </template>

            <!-- Always-on server context -->
            <section class="rounded-xl border border-blue-100 bg-blue-50 px-4 py-3 mt-2">
                <h3 class="text-xs font-bold uppercase tracking-wider text-blue-800 mb-2.5">Servidor propio</h3>
                <ul class="flex flex-col gap-2 text-xs text-slate-700">
                    <li class="flex items-center gap-2"><Server class="w-3.5 h-3.5 text-blue-700" /> Tus datos en tu infraestructura</li>
                    <li class="flex items-center gap-2"><ShieldCheck class="w-3.5 h-3.5 text-blue-700" /> Sin dependencia de terceros</li>
                    <li class="flex items-center gap-2"><Lock class="w-3.5 h-3.5 text-blue-700" /> Cifrado en tránsito (TLS)</li>
                </ul>
            </section>

            <section v-if="metrics.info" class="rounded-xl border border-slate-200 p-3">
                <header class="flex items-center justify-between mb-2">
                    <h3 class="text-xs font-bold uppercase tracking-wider text-slate-500 flex items-center gap-1.5">
                        <BarChart3 class="w-3.5 h-3.5" />
                        Métricas
                    </h3>
                </header>
                <div class="grid grid-cols-2 gap-3 text-xs">
                    <div>
                        <div class="text-slate-500">Usuarios activos</div>
                        <div class="text-base font-semibold text-slate-900">{{ metrics.info.activeUsers }} / {{ metrics.info.maxUsers }}</div>
                    </div>
                    <div>
                        <div class="text-slate-500">Almacenamiento</div>
                        <div class="text-base font-semibold text-slate-900">{{ fmtBytes(metrics.info.storageUsedBytes) }} / {{ fmtBytes(metrics.info.storageQuotaBytes) }}</div>
                    </div>
                </div>
                <div class="mt-2 h-1.5 rounded-full bg-slate-100 overflow-hidden">
                    <div class="h-full bg-blue-600 transition-all" :style="{ width: `${pct()}%` }"></div>
                </div>
            </section>

            <section v-if="license.info" class="rounded-xl border border-slate-200 p-3">
                <h3 class="text-xs font-bold uppercase tracking-wider text-slate-500 mb-2">Licencia</h3>
                <dl class="text-xs text-slate-700 space-y-1">
                    <div class="flex justify-between"><dt>Edición</dt><dd class="font-semibold">{{ isProEdition(license.info) ? 'Pro' : 'Free' }}</dd></div>
                    <div class="flex justify-between"><dt>Máx. usuarios</dt><dd>{{ license.info.maxConcurrentUsers }}</dd></div>
                    <div v-if="license.info.licensedTo" class="flex justify-between"><dt>Licenciada a</dt><dd>{{ license.info.licensedTo }}</dd></div>
                </dl>
            </section>
        </div>
    </aside>
</template>
