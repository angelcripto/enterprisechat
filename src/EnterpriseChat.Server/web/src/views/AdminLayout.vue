<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from "vue";
import { useRoute } from "vue-router";
import { X } from "lucide-vue-next";
import Sidebar from "@/components/Sidebar.vue";
import TopBar from "@/components/TopBar.vue";
import RightPanel from "@/components/RightPanel.vue";
import { useRoomsStore } from "@/stores/rooms";
import { useUsersStore } from "@/stores/users";
import { useLicenseStore } from "@/stores/license";

const rooms = useRoomsStore();
const users = useUsersStore();
const license = useLicenseStore();
const route = useRoute();

const drawerOpen = ref(false);

onMounted(async () => {
    await Promise.allSettled([rooms.load(), users.load(), license.load()]);
    window.addEventListener("keydown", onKey);
});
onBeforeUnmount(() => window.removeEventListener("keydown", onKey));

function onKey(ev: KeyboardEvent): void {
    if (ev.key === "Escape") drawerOpen.value = false;
}
watch(() => route.fullPath, () => { drawerOpen.value = false; });
</script>

<template>
    <!-- Mismo layout que ChatView: TopBar arriba, sidebar 260px, main 1fr, RightPanel 320px. -->
    <!-- Columnas por breakpoint. Antes eran fijas (260px + 320px SIEMPRE), así que
         en ventanas estrechas se reservaban 580px de adorno aunque la barra
         izquierda estuviera oculta (`hidden md:flex`), y lo que sobraba lo
         recortaba el `overflow-hidden` sin dejar barra de scroll. -->
    <div class="h-screen grid grid-cols-[minmax(0,1fr)] md:grid-cols-[260px_minmax(0,1fr)] lg:grid-cols-[260px_minmax(0,1fr)_320px] grid-rows-[56px_1fr] bg-slate-50 overflow-hidden">
        <TopBar class="col-span-full row-start-1 border-b border-slate-200" />
        <Sidebar class="hidden md:flex row-start-2 border-r border-slate-200 overflow-hidden" />

        <Teleport to="body">
            <transition name="drawer">
                <div v-if="drawerOpen" class="md:hidden fixed inset-0 z-40 flex">
                    <div class="fixed inset-0 bg-slate-900/50" @click="drawerOpen = false"></div>
                    <aside class="relative w-72 max-w-[85vw] bg-white shadow-2xl flex z-50">
                        <button type="button" class="absolute top-2 right-2 p-2 rounded-lg hover:bg-slate-100" @click="drawerOpen = false">
                            <X class="w-5 h-5 text-slate-700" />
                        </button>
                        <Sidebar />
                    </aside>
                </div>
            </transition>
        </Teleport>

        <!-- `overflow-y-auto`, NO `overflow-hidden`. Aquí el que scrollea es el
             propio panel: los formularios de administración crecen (p.ej. al
             pulsar "conectar y descubrir esquema" en /manage/auth-providers
             aparecen los campos del esquema) y con `overflow-hidden` lo que
             sobresalía se recortaba sin barra de scroll — inalcanzable.

             Ojo: en el layout del CHAT sí toca `overflow-hidden`, porque allí
             quien scrollea es la lista de mensajes. Son dos casos distintos. -->
        <main class="row-start-2 col-start-1 md:col-start-2 min-w-0 min-h-0 overflow-y-auto flex flex-col">
            <router-view />
        </main>

        <RightPanel class="hidden lg:flex row-start-2 col-start-3 border-l border-slate-200 overflow-hidden" :thread="null" />
    </div>
</template>

<style scoped>
.drawer-enter-active, .drawer-leave-active { transition: opacity 0.15s ease; }
.drawer-enter-from, .drawer-leave-to { opacity: 0; }
</style>
