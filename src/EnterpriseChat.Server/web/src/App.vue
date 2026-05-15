<script setup lang="ts">
import DialogHost from "@/components/dialogs/DialogHost.vue";
// Nota: la sesión se restaura en main.ts ANTES del router, no aquí.
</script>

<template>
    <RouterView />
    <DialogHost />
</template>
