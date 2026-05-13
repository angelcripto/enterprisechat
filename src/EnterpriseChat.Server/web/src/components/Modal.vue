<script setup lang="ts">
import { onBeforeUnmount, onMounted } from "vue";
import { X } from "lucide-vue-next";

/**
 * Generic modal frame. Children render whatever they want inside the body.
 * Closes on Esc and on click outside the dialog. The parent owns the open
 * state and the close handler.
 */
interface Props {
    title?: string;
    width?: string;
}
const props = withDefaults(defineProps<Props>(), { title: "", width: "440px" });
const emit = defineEmits<{ (e: "close"): void }>();

function onKey(ev: KeyboardEvent): void {
    if (ev.key === "Escape") emit("close");
}

onMounted(() => {
    window.addEventListener("keydown", onKey);
    document.body.style.overflow = "hidden";
});
onBeforeUnmount(() => {
    window.removeEventListener("keydown", onKey);
    document.body.style.overflow = "";
});

void props;
</script>

<template>
    <Teleport to="body">
        <div class="fixed inset-0 z-50 grid place-items-center bg-slate-900/50 backdrop-blur-sm p-4" @click.self="$emit('close')">
            <div
                role="dialog"
                aria-modal="true"
                class="bg-white rounded-2xl shadow-2xl overflow-hidden w-full"
                :style="{ maxWidth: width }"
            >
                <header v-if="title" class="flex items-center justify-between px-5 py-4 border-b border-slate-100">
                    <h2 class="text-base font-semibold text-slate-900">{{ title }}</h2>
                    <button type="button" class="text-slate-400 hover:text-slate-700 p-1 rounded hover:bg-slate-100" @click="$emit('close')" aria-label="Cerrar">
                        <X class="w-4 h-4" />
                    </button>
                </header>
                <div class="px-5 py-4">
                    <slot />
                </div>
                <footer v-if="$slots.footer" class="px-5 py-3 border-t border-slate-100 bg-slate-50 flex items-center justify-end gap-2">
                    <slot name="footer" />
                </footer>
            </div>
        </div>
    </Teleport>
</template>
