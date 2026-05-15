<script setup lang="ts">
import { computed } from "vue";
import { useDialogsStore } from "@/stores/dialogs";
import ConfirmDialog from "@/components/dialogs/ConfirmDialog.vue";
import PromptDialog from "@/components/dialogs/PromptDialog.vue";
import AlertDialog from "@/components/dialogs/AlertDialog.vue";
import OptionsDialog from "@/components/dialogs/OptionsDialog.vue";

/**
 * Renderiza el primer diálogo de la cola del store. Cuando se cierra,
 * se elimina y aparece el siguiente. Montar UNA sola vez en App.vue.
 */
const dialogs = useDialogsStore();
const current = computed(() => dialogs.queue[0] ?? null);

function onClose(id: number): void {
    dialogs.pop(id);
}
</script>

<template>
    <template v-if="current">
        <ConfirmDialog
            v-if="current.kind === 'confirm'"
            :key="current.id"
            :request="current"
            @close="onClose(current.id)"
        />
        <PromptDialog
            v-else-if="current.kind === 'prompt'"
            :key="current.id"
            :request="current"
            @close="onClose(current.id)"
        />
        <AlertDialog
            v-else-if="current.kind === 'alert'"
            :key="current.id"
            :request="current"
            @close="onClose(current.id)"
        />
        <OptionsDialog
            v-else-if="current.kind === 'options'"
            :key="current.id"
            :request="current"
            @close="onClose(current.id)"
        />
    </template>
</template>
