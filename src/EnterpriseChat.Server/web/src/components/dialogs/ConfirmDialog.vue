<script setup lang="ts">
import { computed } from "vue";
import { AlertOctagon } from "lucide-vue-next";
import Modal from "@/components/Modal.vue";
import { visualFor } from "@/components/dialogs/dialogVisuals";
import type { ConfirmRequest } from "@/stores/dialogs";

const props = defineProps<{ request: ConfirmRequest }>();
const emit = defineEmits<{ (e: "close"): void }>();

// danger:true fuerza la apariencia roja completa aunque el caller pase otro
// icon — un confirm destructivo se ve siempre con borde rojo + halo rojo +
// AlertOctagon. Sin danger se respeta el icono pedido (question por defecto).
const visual = computed(() => {
    if (props.request.danger) {
        return {
            component: AlertOctagon,
            color: "text-red-600",
            bg: "bg-red-100",
        };
    }
    return visualFor(props.request.icon ?? "question");
});
const danger = computed(() => !!props.request.danger);

function onConfirm(): void {
    props.request.resolve(true);
    emit("close");
}
function onCancel(): void {
    props.request.resolve(false);
    emit("close");
}
</script>

<template>
    <Modal :surface-class="danger ? 'border-2 border-red-500 ring-4 ring-red-100' : undefined" @close="onCancel">
        <div :class="['flex gap-4 items-start', danger ? '-mx-5 -mt-5 px-5 pt-5 pb-4 mb-4 bg-red-50 border-b border-red-200 rounded-t-xl' : '']">
            <div v-if="visual" :class="['w-12 h-12 rounded-full grid place-items-center flex-shrink-0', visual.bg]">
                <component :is="visual.component" :class="['w-7 h-7', visual.color]" />
            </div>
            <div class="flex-1 min-w-0">
                <h3 :class="['text-base font-semibold mb-1', danger ? 'text-red-800' : 'text-slate-900']">
                    {{ request.title }}
                </h3>
                <div v-if="request.html" :class="['text-sm', danger ? 'text-red-900/80' : 'text-slate-600']" v-html="request.html"></div>
                <p v-else-if="request.text" :class="['text-sm whitespace-pre-line', danger ? 'text-red-900/80' : 'text-slate-600']">
                    {{ request.text }}
                </p>
            </div>
        </div>

        <template #footer>
            <button type="button" class="btn btn-secondary" @click="onCancel">
                {{ request.cancelText ?? "Cancelar" }}
            </button>
            <button
                type="button"
                :class="['btn', danger ? 'btn-danger' : 'btn-primary']"
                @click="onConfirm"
            >
                {{ request.confirmText ?? "Aceptar" }}
            </button>
        </template>
    </Modal>
</template>
