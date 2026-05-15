<script setup lang="ts">
import { ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import axios from "axios";
import { Eye, EyeOff, Lock, User, ShieldCheck, Server, Database, Hash, Paperclip, Smile } from "lucide-vue-next";
import { useAuthStore } from "@/stores/auth";

/**
 * Login del server EnterpriseChat. Split-screen: marketing+demo a la
 * izquierda en >=lg, formulario a la derecha. En móvil solo el form.
 *
 * Inspiración del usuario: mock-up generado con ChatGPT con feature
 * cards, mini-preview del propio chat y banner "Servidor operativo".
 * Sin SSO — el server no lo implementa todavía.
 */

const auth = useAuthStore();
const router = useRouter();
const route = useRoute();

const username = ref("");
const password = ref("");
const showPassword = ref(false);
const submitting = ref(false);
const error = ref<string | null>(null);

async function submit(): Promise<void> {
    if (submitting.value) return;
    error.value = null;
    submitting.value = true;
    try {
        await auth.login({ username: username.value.trim(), password: password.value });
        const redirect = typeof route.query.redirect === "string" ? route.query.redirect : "/";
        await router.replace(redirect);
    } catch (err) {
        if (axios.isAxiosError(err) && err.response?.status === 401) {
            error.value = "Usuario o contraseña incorrectos.";
        } else {
            error.value = "No se pudo conectar con el servidor.";
        }
    } finally {
        submitting.value = false;
    }
}
</script>

<template>
    <div class="min-h-screen w-full relative overflow-hidden bg-gradient-to-br from-slate-50 via-blue-50 to-blue-100">
        <!-- Decoración: blobs radiales de fondo. Se renderizan absolutos
             para que el layout siga siendo responsivo y no interfieran. -->
        <div aria-hidden="true" class="pointer-events-none absolute inset-0 overflow-hidden">
            <div class="absolute -top-32 -left-32 w-[480px] h-[480px] rounded-full bg-blue-400/30 blur-3xl"></div>
            <div class="absolute top-1/3 -right-40 w-[520px] h-[520px] rounded-full bg-blue-300/20 blur-3xl"></div>
            <div class="absolute bottom-0 left-1/4 w-[420px] h-[420px] rounded-full bg-indigo-300/20 blur-3xl"></div>
        </div>

        <div class="relative min-h-screen flex flex-col">
            <main class="flex-1 grid lg:grid-cols-2 gap-6 lg:gap-10 px-4 sm:px-8 lg:px-12 py-6 lg:py-10">

                <!-- LADO IZQUIERDO: marketing + mock-up. Solo visible en lg+. -->
                <section class="hidden lg:flex flex-col rounded-3xl bg-gradient-to-br from-blue-50/60 via-white/40 to-blue-50/60 backdrop-blur-sm border border-white/60 shadow-xl p-10">
                    <header class="flex flex-col gap-1 mb-8">
                        <img src="/logo-enterprisechat.png" alt="EnterpriseChat" class="h-10 w-auto" />
                        <p class="text-xs text-slate-500 ml-1">Comunicación interna autoalojada</p>
                    </header>

                    <h1 class="text-3xl xl:text-4xl font-bold text-slate-900 leading-tight mb-3">
                        La comunicación interna<br />
                        segura, privada y bajo tu control.
                    </h1>
                    <p class="text-sm text-slate-600 max-w-md mb-8">
                        EnterpriseChat es la plataforma de mensajería para equipos que priorizan la privacidad, la seguridad y la soberanía de sus datos.
                    </p>

                    <!-- Mini-preview del chat en CSS puro. Para evitar
                         depender de una captura externa que envejecería. -->
                    <div class="rounded-2xl bg-white shadow-xl border border-slate-200 overflow-hidden mb-6 max-w-xl">
                        <div class="grid grid-cols-[180px_1fr] h-[280px]">
                            <aside class="bg-slate-50 border-r border-slate-200 p-3 text-xs flex flex-col gap-1">
                                <span class="text-[10px] uppercase tracking-wider font-semibold text-slate-400 mb-1">Canales</span>
                                <div class="px-2 py-1 rounded bg-blue-50 text-blue-700 font-semibold flex items-center gap-1.5"><Hash class="w-3 h-3" /> general</div>
                                <div class="px-2 py-1 text-slate-600 flex items-center gap-1.5"><Hash class="w-3 h-3" /> proyectos</div>
                                <div class="px-2 py-1 text-slate-600 flex items-center gap-1.5"><Hash class="w-3 h-3" /> soporte</div>
                                <div class="px-2 py-1 text-slate-600 flex items-center gap-1.5"><Hash class="w-3 h-3" /> anuncios</div>
                                <span class="text-[10px] uppercase tracking-wider font-semibold text-slate-400 mt-3 mb-1">Mensajes directos</span>
                                <div class="px-2 py-1 text-slate-600 flex items-center gap-1.5">
                                    <span class="w-1.5 h-1.5 rounded-full bg-emerald-500"></span> Laura Martínez
                                </div>
                                <div class="px-2 py-1 text-slate-600 flex items-center gap-1.5">
                                    <span class="w-1.5 h-1.5 rounded-full bg-emerald-500"></span> Diego Romero
                                </div>
                                <div class="px-2 py-1 text-slate-600 flex items-center gap-1.5">
                                    <span class="w-1.5 h-1.5 rounded-full bg-slate-300"></span> Ana García
                                </div>
                            </aside>
                            <div class="flex flex-col">
                                <div class="px-4 py-2 border-b border-slate-100 text-xs font-semibold text-slate-900 flex items-center gap-1.5">
                                    <Hash class="w-3 h-3 text-slate-400" /> general
                                </div>
                                <div class="flex-1 p-3 flex flex-col gap-3 overflow-hidden">
                                    <div class="flex gap-2">
                                        <span class="w-7 h-7 rounded-full bg-rose-200 grid place-items-center text-[10px] font-bold text-rose-800 flex-shrink-0">LM</span>
                                        <div>
                                            <div class="text-[11px]"><strong class="text-slate-900">Laura Martínez</strong> <span class="text-slate-400">09:15</span></div>
                                            <p class="text-xs text-slate-700">¡Buenos días, equipo! 👋<br />Repasamos los objetivos del sprint.</p>
                                        </div>
                                    </div>
                                    <div class="flex gap-2">
                                        <span class="w-7 h-7 rounded-full bg-emerald-200 grid place-items-center text-[10px] font-bold text-emerald-800 flex-shrink-0">DR</span>
                                        <div>
                                            <div class="text-[11px]"><strong class="text-slate-900">Diego Romero</strong> <span class="text-slate-400">09:16</span></div>
                                            <p class="text-xs text-slate-700">De acuerdo, aquí va el resumen de lo avanzado.</p>
                                            <div class="mt-1 inline-flex items-center gap-2 px-2 py-1 rounded bg-slate-100 text-[11px] text-slate-700">
                                                <Paperclip class="w-3 h-3 text-slate-500" />
                                                <span class="font-medium">Resumen-sprint.pdf</span>
                                                <span class="text-slate-400">1.2 MB</span>
                                            </div>
                                        </div>
                                    </div>
                                </div>
                                <div class="px-3 py-2 border-t border-slate-100 flex items-center gap-2">
                                    <input type="text" class="flex-1 text-xs px-2 py-1 rounded bg-slate-50 border border-slate-200 text-slate-500" placeholder="Escribe un mensaje…" disabled />
                                    <Paperclip class="w-3.5 h-3.5 text-slate-400" />
                                    <Smile class="w-3.5 h-3.5 text-slate-400" />
                                </div>
                            </div>
                        </div>
                    </div>

                    <!-- Feature cards. -->
                    <div class="grid grid-cols-3 gap-3 mb-4 max-w-xl">
                        <div class="rounded-xl bg-white/80 backdrop-blur border border-slate-200 p-3">
                            <Server class="w-4 h-4 text-blue-600 mb-1.5" />
                            <h4 class="text-xs font-bold text-slate-900 mb-0.5">Autoalojado</h4>
                            <p class="text-[10px] text-slate-500 leading-tight">Instálalo en tu infraestructura. Tú mantienes el control.</p>
                        </div>
                        <div class="rounded-xl bg-white/80 backdrop-blur border border-slate-200 p-3">
                            <Lock class="w-4 h-4 text-blue-600 mb-1.5" />
                            <h4 class="text-xs font-bold text-slate-900 mb-0.5">Cifrado en tránsito</h4>
                            <p class="text-[10px] text-slate-500 leading-tight">HTTPS y JWT. Tus conversaciones siempre protegidas.</p>
                        </div>
                        <div class="rounded-xl bg-white/80 backdrop-blur border border-slate-200 p-3">
                            <Database class="w-4 h-4 text-blue-600 mb-1.5" />
                            <h4 class="text-xs font-bold text-slate-900 mb-0.5">Control de datos</h4>
                            <p class="text-[10px] text-slate-500 leading-tight">Soberanía, cumplimiento y privacidad garantizadas.</p>
                        </div>
                    </div>

                    <!-- Banner servidor operativo. -->
                    <div class="rounded-xl bg-white/80 backdrop-blur border border-slate-200 p-3 flex items-center justify-between max-w-xl">
                        <div class="flex items-center gap-2">
                            <span class="relative flex w-2.5 h-2.5">
                                <span class="absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75 animate-ping"></span>
                                <span class="relative inline-flex w-2.5 h-2.5 rounded-full bg-emerald-500"></span>
                            </span>
                            <div>
                                <div class="text-xs font-semibold text-slate-900">Servidor operativo</div>
                                <div class="text-[10px] text-slate-500">Todos los sistemas funcionando con normalidad.</div>
                            </div>
                        </div>
                    </div>
                </section>

                <!-- LADO DERECHO: formulario de login. -->
                <section class="flex items-center justify-center">
                    <div class="w-full max-w-md">
                        <!-- Logo móvil (solo <lg). -->
                        <div class="flex lg:hidden justify-center mb-6">
                            <img src="/logo-enterprisechat.png" alt="EnterpriseChat" class="h-9 w-auto" />
                        </div>

                        <div class="card bg-white/90 backdrop-blur-xl rounded-3xl shadow-2xl border border-white/60 p-7 sm:p-9">
                            <div class="hidden lg:flex justify-center mb-6">
                                <img src="/logo-enterprisechat.png" alt="EnterpriseChat" class="h-11 w-auto" />
                            </div>

                            <h1 class="text-2xl sm:text-3xl font-bold text-slate-900 text-center mb-1">Bienvenido</h1>
                            <p class="text-sm text-slate-500 text-center mb-6">Accede a tu espacio de trabajo seguro.</p>

                            <form @submit.prevent="submit" class="flex flex-col gap-4" autocomplete="on">
                                <label class="flex flex-col gap-1.5">
                                    <span class="text-sm font-medium text-slate-700">Usuario</span>
                                    <div class="relative">
                                        <User class="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
                                        <input
                                            v-model="username"
                                            type="text"
                                            required
                                            autocomplete="username"
                                            class="input pl-10"
                                            placeholder="usuario@empresa.com"
                                        />
                                    </div>
                                </label>

                                <label class="flex flex-col gap-1.5">
                                    <span class="text-sm font-medium text-slate-700">Contraseña</span>
                                    <div class="relative">
                                        <Lock class="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
                                        <input
                                            v-model="password"
                                            :type="showPassword ? 'text' : 'password'"
                                            required
                                            autocomplete="current-password"
                                            class="input pl-10 pr-10"
                                            placeholder="Tu contraseña"
                                        />
                                        <button
                                            type="button"
                                            class="absolute right-2 top-1/2 -translate-y-1/2 p-1.5 text-slate-400 hover:text-slate-700 rounded"
                                            @click="showPassword = !showPassword"
                                            :aria-label="showPassword ? 'Ocultar contraseña' : 'Mostrar contraseña'"
                                        >
                                            <component :is="showPassword ? EyeOff : Eye" class="w-4 h-4" />
                                        </button>
                                    </div>
                                </label>

                                <p v-if="error" class="text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg px-3 py-2">{{ error }}</p>

                                <button
                                    type="submit"
                                    class="btn btn-primary mt-2 py-3 text-base font-semibold rounded-xl"
                                    :disabled="submitting"
                                >
                                    <span v-if="submitting">Entrando…</span>
                                    <span v-else>Entrar</span>
                                </button>

                                <p class="text-xs text-slate-500 text-center pt-1">
                                    ¿Olvidaste tu contraseña? Pide a tu administrador que la restablezca.
                                </p>
                            </form>

                            <div class="mt-6 pt-5 border-t border-slate-100 flex items-center justify-center gap-3 text-[11px] text-slate-500">
                                <span class="inline-flex items-center gap-1">
                                    <ShieldCheck class="w-3 h-3 text-slate-400" />
                                    Acceso seguro
                                </span>
                                <span class="text-slate-300">·</span>
                                <span>Gestionado por tu organización</span>
                            </div>
                        </div>
                    </div>
                </section>
            </main>

            <footer class="relative px-4 sm:px-8 lg:px-12 py-4 text-[11px] text-slate-500 flex flex-col sm:flex-row items-center justify-center sm:justify-between gap-2">
                <span>© {{ new Date().getFullYear() }} EnterpriseChat. Todos los derechos reservados.</span>
                <span class="flex items-center gap-3">
                    <a href="https://enterprisechat.es" target="_blank" rel="noopener" class="hover:text-slate-900">Web</a>
                    <span class="text-slate-300">·</span>
                    <a href="https://enterprisechat.es/docs" target="_blank" rel="noopener" class="hover:text-slate-900">Documentación</a>
                </span>
            </footer>
        </div>
    </div>
</template>
