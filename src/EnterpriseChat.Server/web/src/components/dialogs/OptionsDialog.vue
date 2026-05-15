<script setup lang="ts">
import { computed, ref } from "vue";
import Modal from "@/components/Modal.vue";
import { visualFor } from "@/components/dialogs/dialogVisuals";
import type { OptionsRequest } from "@/stores/dialogs";

const props = defineProps<{ request: OptionsRequest }>();
const emit = defineEmits<{ (e: "close"): void }>();

const visual = computed(() => visualFor(props.request.icon ?? "question"));
const selected = ref<string>(props.request.defaultValue ?? props.request.options[0]?.value ?? "");

function confirm(): void {
    props.request.resolve(selected.value);
    emit("close");
}
function cancel(): void {
    props.request.resolve(null);
    emit("close");
}
</script>

<template>
    <Modal :title="request.title" width="540px" @close="cancel">
        <div class="flex gap-4 items-start mb-3">
            <div v-if="visual" :class="['w-10 h-10 rounded-full grid place-items-center flex-shrink-0', visual.bg]">
                <component :is="visual.component" :class="['w-6 h-6', visual.color]" />
            </div>
            <div class="flex-1 min-w-0">
                <div v-if="request.html" class="text-sm text-slate-600" v-html="request.html"></div>
                <p v-else-if="request.text" class="text-sm text-slate-600 whitespace-pre-line">{{ request.text }}</p>
            </div>
        </div>

        <div class="flex flex-col gap-2">
            <label
                v-for="opt in request.options"
                :key="opt.value"
                :class="[
                    'flex items-start gap-3 p-3 rounded-lg border cursor-pointer transition',
                    selected === opt.value
                        ? (opt.danger ? 'border-red-300 bg-red-50' : 'border-blue-300 bg-blue-50')
                        : 'border-slate-200 hover:bg-slate-50',
                ]"
            >
                <input type="radio" :value="opt.value" v-model="selected" class="mt-1" />
                <div class="flex-1 min-w-0">
                    <div :class="['text-sm font-medium', opt.danger ? 'text-red-700' : 'text-slate-900']">{{ opt.label }}</div>
                    <div v-if="opt.description" class="text-xs text-slate-500 mt-0.5">{{ opt.description }}</div>
                </div>
            </label>
        </div>

        <template #footer>
            <button type="button" class="btn btn-secondary" @click="cancel">{{ request.cancelText ?? "Cancelar" }}</button>
            <button type="button" class="btn btn-primary" @click="confirm">{{ request.confirmText ?? "Aceptar" }}</button>
        </template>
    </Modal>
</template>
