<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted } from "vue";
import Modal from "@/components/Modal.vue";
import { visualFor } from "@/components/dialogs/dialogVisuals";
import type { AlertRequest } from "@/stores/dialogs";

const props = defineProps<{ request: AlertRequest }>();
const emit = defineEmits<{ (e: "close"): void }>();

const visual = computed(() => visualFor(props.request.icon));

let timer = 0;
onMounted(() => {
    if (props.request.autoCloseMs && props.request.autoCloseMs > 0) {
        timer = window.setTimeout(close, props.request.autoCloseMs);
    }
});
onBeforeUnmount(() => {
    if (timer !== 0) window.clearTimeout(timer);
});

function close(): void {
    props.request.resolve();
    emit("close");
}
</script>

<template>
    <Modal @close="close">
        <div class="flex gap-4 items-start">
            <div v-if="visual" :class="['w-10 h-10 rounded-full grid place-items-center flex-shrink-0', visual.bg]">
                <component :is="visual.component" :class="['w-6 h-6', visual.color]" />
            </div>
            <div class="flex-1 min-w-0">
                <h3 class="text-base font-semibold text-slate-900 mb-1">{{ request.title }}</h3>
                <div v-if="request.html" class="text-sm text-slate-600" v-html="request.html"></div>
                <p v-else-if="request.text" class="text-sm text-slate-600 whitespace-pre-line">{{ request.text }}</p>
            </div>
        </div>
        <template #footer>
            <button type="button" class="btn btn-primary" @click="close">{{ request.confirmText ?? "OK" }}</button>
        </template>
    </Modal>
</template>
