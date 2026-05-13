<script setup lang="ts">
import { computed, ref, watch } from "vue";

/**
 * Renders a circular user avatar. Server URL is /users/{id}/avatar; if the
 * GET 404s (user without uploaded picture) we fall back to coloured initials.
 *
 * The `hasAvatar` hint comes from /users so we avoid even firing the request
 * when we already know there is no avatar — keeps the network panel tidy.
 */
interface Props {
    userId: number;
    fullName: string;
    hasAvatar?: boolean;
    size?: number;
    showStatus?: boolean;
    online?: boolean;
}

const props = withDefaults(defineProps<Props>(), {
    hasAvatar: false,
    size: 36,
    showStatus: false,
    online: false,
});

const failed = ref(false);

watch(() => [props.userId, props.hasAvatar], () => { failed.value = false; });

const showImage = computed(() => props.hasAvatar && !failed.value);

const initials = computed(() => {
    return props.fullName
        .split(/\s+/)
        .filter(Boolean)
        .slice(0, 2)
        .map((p) => p[0])
        .join("")
        .toUpperCase() || "?";
});

/** Deterministic colour per user so renderings stay stable across sessions. */
const bg = computed(() => {
    const palette = [
        "bg-blue-500", "bg-emerald-500", "bg-amber-500", "bg-rose-500",
        "bg-violet-500", "bg-cyan-500", "bg-indigo-500", "bg-fuchsia-500",
    ];
    return palette[Math.abs(props.userId) % palette.length];
});

const px = computed(() => `${props.size}px`);
const ring = computed(() => `${Math.max(2, Math.round(props.size / 18))}px`);
</script>

<template>
    <span class="relative inline-block flex-shrink-0" :style="{ width: px, height: px }">
        <img
            v-if="showImage"
            :src="`/users/${userId}/avatar`"
            :alt="fullName"
            class="w-full h-full rounded-full object-cover"
            @error="failed = true"
        />
        <span
            v-else
            :class="['w-full h-full rounded-full grid place-items-center text-white font-semibold', bg]"
            :style="{ fontSize: `${Math.round(size * 0.4)}px` }"
        >{{ initials }}</span>
        <span
            v-if="showStatus"
            class="absolute bottom-0 right-0 rounded-full border-2 border-white"
            :class="online ? 'bg-emerald-500' : 'bg-slate-300'"
            :style="{ width: `${Math.round(size * 0.32)}px`, height: `${Math.round(size * 0.32)}px`, borderWidth: ring }"
        ></span>
    </span>
</template>
