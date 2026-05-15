<script setup lang="ts">
import { ref } from "vue";
import { AlertTriangle, Copy, Check } from "lucide-vue-next";
import Modal from "@/components/Modal.vue";
import type { IssuedApiKey } from "@/stores/apiKeys";

interface Props {
    issued: IssuedApiKey;
    /** Texto del título: "Nueva clave creada" vs "Clave rotada". */
    title?: string;
}

const props = withDefaults(defineProps<Props>(), { title: "Nueva clave creada" });

const emit = defineEmits<{
    (e: "close"): void;
}>();

const copied = ref(false);

async function copyToClipboard(): Promise<void> {
    try {
        await navigator.clipboard.writeText(props.issued.plaintext);
        copied.value = true;
        window.setTimeout(() => { copied.value = false; }, 2000);
    } catch {
        // Fallback: seleccionar el texto en el textarea para que el usuario
        // pueda Ctrl+C manualmente — la API clipboard puede fallar en
        // contextos sin HTTPS (http://servidor:5080 desde otra máquina).
        const el = document.getElementById("issued-plaintext") as HTMLTextAreaElement | null;
        el?.focus();
        el?.select();
    }
}
</script>

<template>
    <Modal :title="title" width="560px" @close="emit('close')">
        <div class="flex flex-col gap-4">
            <div class="flex items-start gap-2 bg-amber-50 border border-amber-200 rounded-lg px-3 py-2.5 text-sm text-amber-900">
                <AlertTriangle class="w-4 h-4 flex-shrink-0 mt-0.5 text-amber-600" />
                <div>
                    <strong class="block font-semibold">Esta es la única vez que verás el secreto.</strong>
                    Cópialo ahora a un gestor de contraseñas o variable de entorno.
                    Si lo pierdes, tendrás que rotar la clave para conseguir uno nuevo.
                </div>
            </div>

            <label class="flex flex-col gap-1">
                <span class="text-xs font-bold uppercase tracking-wider text-slate-500">Token</span>
                <div class="relative">
                    <textarea
                        id="issued-plaintext"
                        :value="issued.plaintext"
                        readonly
                        rows="2"
                        spellcheck="false"
                        class="w-full font-mono text-sm bg-slate-900 text-emerald-200 border border-slate-700 rounded-lg px-3 py-2.5 pr-24 resize-none focus:outline-none focus:ring-2 focus:ring-blue-500"
                    />
                    <button
                        type="button"
                        class="absolute top-2 right-2 inline-flex items-center gap-1.5 text-xs font-medium px-2.5 py-1.5 rounded-md bg-slate-700 hover:bg-slate-600 text-slate-100"
                        @click="copyToClipboard"
                    >
                        <component :is="copied ? Check : Copy" class="w-3.5 h-3.5" />
                        {{ copied ? "Copiado" : "Copiar" }}
                    </button>
                </div>
            </label>

            <dl class="grid grid-cols-2 gap-3 text-sm">
                <div>
                    <dt class="text-slate-500 text-xs uppercase tracking-wider">Prefijo</dt>
                    <dd class="font-mono text-slate-900">{{ issued.key.prefix }}</dd>
                </div>
                <div>
                    <dt class="text-slate-500 text-xs uppercase tracking-wider">Rol</dt>
                    <dd>
                        <span :class="['px-2 py-0.5 rounded-full text-xs font-bold',
                                       issued.key.role === 'Admin' ? 'bg-blue-100 text-blue-800' : 'bg-slate-100 text-slate-600']">
                            {{ issued.key.role }}
                        </span>
                    </dd>
                </div>
                <div v-if="issued.key.expiresAt">
                    <dt class="text-slate-500 text-xs uppercase tracking-wider">Caduca</dt>
                    <dd class="text-slate-900">{{ new Date(issued.key.expiresAt).toLocaleString("es-ES") }}</dd>
                </div>
            </dl>

            <p class="text-xs text-slate-600">
                Úsala como <code class="bg-slate-100 px-1.5 py-0.5 rounded">Authorization: Bearer {{ issued.key.prefix }}…</code>
                en tus peticiones REST. En <code class="bg-slate-100 px-1.5 py-0.5 rounded">/files</code>
                acepta también <code class="bg-slate-100 px-1.5 py-0.5 rounded">?api_key=…</code>.
            </p>
        </div>

        <template #footer>
            <button type="button" class="btn btn-primary" @click="emit('close')">
                He copiado el token, cerrar
            </button>
        </template>
    </Modal>
</template>
