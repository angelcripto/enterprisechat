<script setup lang="ts">
import { nextTick, onMounted, ref } from "vue";
import Modal from "@/components/Modal.vue";
import type { PromptRequest } from "@/stores/dialogs";

const props = defineProps<{ request: PromptRequest }>();
const emit = defineEmits<{ (e: "close"): void }>();

const value = ref(props.request.initial ?? "");
const error = ref<string | null>(null);
const inputEl = ref<HTMLInputElement | null>(null);

onMounted(async () => {
    await nextTick();
    inputEl.value?.focus();
    inputEl.value?.select();
});

function submit(): void {
    const v = value.value;
    if (props.request.validator) {
        const msg = props.request.validator(v);
        if (msg) { error.value = msg; return; }
    }
    props.request.resolve(v);
    emit("close");
}

function cancel(): void {
    props.request.resolve(null);
    emit("close");
}
</script>

<template>
    <Modal :title="request.title" @close="cancel">
        <p v-if="request.text" class="text-sm text-slate-600 mb-3">{{ request.text }}</p>

        <label class="flex flex-col gap-1">
            <span v-if="request.label" class="text-sm font-medium text-slate-700">{{ request.label }}</span>
            <input
                ref="inputEl"
                v-model="value"
                :type="request.inputType ?? 'text'"
                class="input"
                :placeholder="request.placeholder ?? ''"
                autocomplete="off"
                @keydown.enter.prevent="submit"
            />
        </label>
        <p v-if="error" class="mt-2 text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg px-3 py-2">{{ error }}</p>

        <template #footer>
            <button type="button" class="btn btn-secondary" @click="cancel">{{ request.cancelText ?? "Cancelar" }}</button>
            <button type="button" class="btn btn-primary" @click="submit">{{ request.confirmText ?? "Aceptar" }}</button>
        </template>
    </Modal>
</template>
