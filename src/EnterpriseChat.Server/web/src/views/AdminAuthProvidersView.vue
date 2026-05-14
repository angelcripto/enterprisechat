<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { ArrowLeft, ShieldCheck, AlertTriangle, Plus, Pencil, Trash2, Plug, Save } from "lucide-vue-next";
import { api } from "@/api/client";
import { useAuthStore } from "@/stores/auth";
import { dialogConfirm, dialogError, dialogSuccess } from "@/dialogs";

/**
 * Gestión de proveedores de autenticación externos. El usuario admin
 * SIEMPRE va por el provider interno (SQLite local), por seguridad. Esta
 * pantalla permite encadenar proveedores adicionales para que los
 * empleados de la empresa puedan iniciar sesión usando una BD que ya
 * tienen montada (típicamente MySQL del CRM, intranet, helpdesk, etc.)
 * sin tener que migrar las credenciales.
 */

interface AuthProviderSummary {
    id: number;
    kind: string;
    displayName: string;
    isEnabled: boolean;
    priority: number;
    hashAlgorithm: string;
    plaintextRiskAcknowledged: boolean;
    createdAt: string;
    updatedAt: string;
}

interface MySqlConfig {
    host: string;
    port: number;
    database: string;
    table: string;
    usernameColumn: string;
    passwordColumn: string;
    externalIdColumn: string | null;
    fullNameColumn: string | null;
    emailColumn: string | null;
    extraWhere: string | null;
    tlsMode: number;
    autoProvision: boolean;
    queryTimeoutSeconds: number;
}

interface MySqlSecrets {
    user: string;
    password: string;
    caBundlePem: string | null;
}

const HASH_OPTIONS = [
    { value: "Bcrypt",       label: "BCrypt ($2a$/$2b$/$2y$)" },
    { value: "Argon2id",     label: "Argon2id (formato PHC)" },
    { value: "Sha256Salted", label: "SHA-256 con sal (estilo Django/Laravel)" },
    { value: "Sha256",       label: "SHA-256 hex sin sal (inseguro)" },
    { value: "Sha1",         label: "SHA-1 hex sin sal (inseguro)" },
    { value: "Md5",          label: "MD5 hex sin sal (inseguro y roto)" },
    { value: "Plaintext",    label: "Texto plano (compatibilidad con servidores antiguos)" },
];

const TLS_OPTIONS = [
    { value: 4, label: "VerifyFull · valida cert y hostname (recomendado)" },
    { value: 3, label: "VerifyCA · valida solo cadena" },
    { value: 2, label: "Required · cifrado sin validar" },
    { value: 1, label: "Preferred · prueba TLS, cae a claro" },
    { value: 0, label: "None · sin cifrado (solo redes confiables)" },
];

const auth = useAuthStore();
const router = useRouter();

const list = ref<AuthProviderSummary[]>([]);
const loading = ref(false);
const editing = ref<AuthProviderSummary | null>(null);
const isNew = ref(false);

const form = ref<{
    displayName: string;
    isEnabled: boolean;
    priority: number;
    hashAlgorithm: string;
    plaintextRiskAcknowledged: boolean;
    config: MySqlConfig;
    secrets: MySqlSecrets;
}>({
    displayName: "",
    isEnabled: true,
    priority: 100,
    hashAlgorithm: "Bcrypt",
    plaintextRiskAcknowledged: false,
    config: blankConfig(),
    secrets: blankSecrets(),
});

const testResult = ref<{ connected: boolean; userFound: boolean | null; detail: string | null } | null>(null);
const testProbeUsername = ref("");
const submitting = ref(false);

const showPlaintextWarning = computed(() => form.value.hashAlgorithm === "Plaintext");

function blankConfig(): MySqlConfig {
    return {
        host: "",
        port: 3306,
        database: "",
        table: "users",
        usernameColumn: "username",
        passwordColumn: "password_hash",
        externalIdColumn: null,
        fullNameColumn: null,
        emailColumn: null,
        extraWhere: null,
        tlsMode: 4,
        autoProvision: true,
        queryTimeoutSeconds: 5,
    };
}

function blankSecrets(): MySqlSecrets {
    return { user: "", password: "", caBundlePem: null };
}

async function refresh(): Promise<void> {
    loading.value = true;
    try {
        const { data } = await api.get<AuthProviderSummary[]>("/admin/auth-providers/");
        list.value = data;
    } finally {
        loading.value = false;
    }
}

onMounted(async () => {
    if (!auth.isAdmin) {
        await router.replace({ name: "inbox" });
        return;
    }
    await refresh();
});

async function openNew(): Promise<void> {
    isNew.value = true;
    editing.value = null;
    form.value = {
        displayName: "MySQL externo",
        isEnabled: true,
        priority: list.value.length === 0 ? 100 : Math.max(...list.value.map((p) => p.priority)) + 10,
        hashAlgorithm: "Bcrypt",
        plaintextRiskAcknowledged: false,
        config: blankConfig(),
        secrets: blankSecrets(),
    };
    testResult.value = null;
}

async function openEdit(row: AuthProviderSummary): Promise<void> {
    const { data } = await api.get<{ config: MySqlConfig }>(`/admin/auth-providers/${row.id}`);
    isNew.value = false;
    editing.value = row;
    form.value = {
        displayName: row.displayName,
        isEnabled: row.isEnabled,
        priority: row.priority,
        hashAlgorithm: row.hashAlgorithm,
        plaintextRiskAcknowledged: row.plaintextRiskAcknowledged,
        config: { ...blankConfig(), ...data.config },
        secrets: blankSecrets(), // Nunca devolvemos secretos del server.
    };
    testResult.value = null;
}

function closeEditor(): void {
    isNew.value = false;
    editing.value = null;
}

function payloadForSave(): {
    displayName: string;
    isEnabled: boolean;
    priority: number;
    hashAlgorithm: string;
    plaintextRiskAcknowledged: boolean;
    config: MySqlConfig;
    secrets: MySqlSecrets | null;
} {
    const f = form.value;
    const includeSecrets = isNew.value
        || f.secrets.user.length > 0
        || f.secrets.password.length > 0
        || (f.secrets.caBundlePem ?? "").length > 0;
    return {
        displayName: f.displayName,
        isEnabled: f.isEnabled,
        priority: f.priority,
        hashAlgorithm: f.hashAlgorithm,
        plaintextRiskAcknowledged: f.plaintextRiskAcknowledged,
        config: f.config,
        secrets: includeSecrets ? f.secrets : null,
    };
}

async function save(): Promise<void> {
    if (submitting.value) return;
    submitting.value = true;
    try {
        const payload = payloadForSave();
        if (isNew.value) {
            await api.post("/admin/auth-providers/", { kind: "Mysql", ...payload });
            await dialogSuccess("Proveedor creado");
        } else if (editing.value) {
            await api.put(`/admin/auth-providers/${editing.value.id}`, payload);
            await dialogSuccess("Proveedor actualizado");
        }
        closeEditor();
        await refresh();
    } catch (err: unknown) {
        const e = err as { response?: { data?: { error?: string } }, message?: string };
        await dialogError("No se pudo guardar", e.response?.data?.error ?? e.message ?? String(err));
    } finally {
        submitting.value = false;
    }
}

async function remove(row: AuthProviderSummary): Promise<void> {
    const ok = await dialogConfirm({
        title: `¿Eliminar el proveedor "${row.displayName}"?`,
        text: "Los usuarios provisionados por este proveedor seguirán existiendo en la base local (su columna SourceProviderId quedará a null), pero no podrán autenticarse por esta vía.",
        confirmText: "Eliminar",
        cancelText: "Cancelar",
        danger: true,
    });
    if (!ok) return;
    try {
        await api.delete(`/admin/auth-providers/${row.id}`);
        await refresh();
    } catch (err) {
        await dialogError("No se pudo eliminar", err instanceof Error ? err.message : String(err));
    }
}

async function testConnection(): Promise<void> {
    testResult.value = null;
    try {
        const payload = {
            kind: "Mysql",
            config: form.value.config,
            secrets: form.value.secrets,
            testUsername: testProbeUsername.value.trim() || null,
        };
        const { data } = await api.post<{ connected: boolean; userFound: boolean | null; detail: string | null }>(
            "/admin/auth-providers/test", payload);
        testResult.value = data;
    } catch (err: unknown) {
        const e = err as { response?: { data?: { error?: string } }, message?: string };
        testResult.value = { connected: false, userFound: null, detail: e.response?.data?.error ?? e.message ?? String(err) };
    }
}
</script>

<template>
    <div class="min-h-screen bg-slate-50 px-6 py-8">
        <div class="max-w-5xl mx-auto">
            <button type="button" @click="router.back()" class="inline-flex items-center gap-1.5 text-sm text-slate-600 hover:text-slate-900 mb-6">
                <ArrowLeft class="w-4 h-4" />
                Volver
            </button>

            <header class="mb-6">
                <h1 class="text-2xl font-bold text-slate-900 flex items-center gap-2">
                    <ShieldCheck class="w-6 h-6 text-blue-600" />
                    Autenticación externa
                </h1>
                <p class="text-sm text-slate-600 mt-1">
                    Permite a tus empleados iniciar sesión con las credenciales que ya usan en tu BD existente.
                    El usuario <code class="px-1 bg-slate-100 rounded">admin</code> siempre se valida contra la base local — no se puede mover fuera.
                </p>
            </header>

            <div class="rounded-lg border border-amber-200 bg-amber-50 p-4 mb-6 flex items-start gap-3 text-sm text-amber-900">
                <AlertTriangle class="w-5 h-5 flex-shrink-0 mt-0.5" />
                <div>
                    <strong>Avisos de seguridad:</strong>
                    <ul class="list-disc list-inside mt-1 space-y-1">
                        <li>Las credenciales que pegues aquí quedan cifradas con AES-256-GCM en disco, pero la copia ofuscada
                            está al alcance de cualquier proceso con acceso al filesystem del server.</li>
                        <li>Crea un usuario MySQL específico con permisos SELECT exclusivos sobre la tabla configurada y, si tu MySQL lo soporta, restringe por IP origen.</li>
                        <li>Considera tunelizar el tráfico por VPN si tu MySQL no expone TLS.</li>
                    </ul>
                </div>
            </div>

            <section class="card p-6 bg-white mb-6" v-if="!isNew && !editing">
                <div class="flex items-center justify-between mb-4">
                    <h2 class="text-sm font-bold uppercase tracking-wider text-slate-500">Proveedores configurados</h2>
                    <button type="button" class="btn btn-primary text-sm" @click="openNew">
                        <Plus class="w-4 h-4" />
                        Añadir proveedor
                    </button>
                </div>

                <div v-if="loading" class="text-sm text-slate-500">Cargando…</div>
                <div v-else-if="list.length === 0" class="text-sm text-slate-500">
                    Aún no hay proveedores externos. Pulsa "Añadir" para encadenar uno.
                </div>
                <table v-else class="w-full text-sm">
                    <thead class="text-left text-xs uppercase tracking-wider text-slate-500 border-b">
                        <tr>
                            <th class="py-2">Nombre</th>
                            <th class="py-2">Tipo</th>
                            <th class="py-2">Algoritmo</th>
                            <th class="py-2">Prioridad</th>
                            <th class="py-2">Estado</th>
                            <th class="py-2"></th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="row in list" :key="row.id" class="border-b border-slate-100">
                            <td class="py-3 font-medium text-slate-900">{{ row.displayName }}</td>
                            <td class="py-3">{{ row.kind }}</td>
                            <td class="py-3">{{ row.hashAlgorithm }}</td>
                            <td class="py-3">{{ row.priority }}</td>
                            <td class="py-3">
                                <span :class="[
                                    'px-2 py-0.5 rounded-full text-xs font-bold',
                                    row.isEnabled ? 'bg-emerald-100 text-emerald-800' : 'bg-slate-100 text-slate-600',
                                ]">{{ row.isEnabled ? 'Activo' : 'Desactivado' }}</span>
                            </td>
                            <td class="py-3 text-right space-x-2">
                                <button class="btn btn-secondary text-xs" @click="openEdit(row)"><Pencil class="w-3.5 h-3.5" /> Editar</button>
                                <button class="btn btn-secondary text-xs text-red-700 border-red-200 hover:bg-red-50" @click="remove(row)"><Trash2 class="w-3.5 h-3.5" /> Eliminar</button>
                            </td>
                        </tr>
                    </tbody>
                </table>
            </section>

            <section v-if="isNew || editing" class="card p-6 bg-white">
                <h2 class="text-sm font-bold uppercase tracking-wider text-slate-500 mb-4">
                    {{ isNew ? "Nuevo proveedor MySQL" : `Editar: ${editing?.displayName}` }}
                </h2>

                <div class="grid sm:grid-cols-2 gap-4">
                    <label class="flex flex-col gap-1">
                        <span class="text-sm font-medium text-slate-700">Nombre visible</span>
                        <input v-model="form.displayName" type="text" class="input" placeholder="MySQL intranet" />
                    </label>
                    <label class="flex flex-col gap-1">
                        <span class="text-sm font-medium text-slate-700">Prioridad (menor = antes)</span>
                        <input v-model.number="form.priority" type="number" class="input" />
                    </label>
                    <label class="flex flex-col gap-1">
                        <span class="text-sm font-medium text-slate-700">Algoritmo del hash almacenado</span>
                        <select v-model="form.hashAlgorithm" class="input">
                            <option v-for="o in HASH_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                        </select>
                    </label>
                    <label class="flex items-center gap-2 mt-6">
                        <input type="checkbox" v-model="form.isEnabled" />
                        <span class="text-sm">Habilitado</span>
                    </label>
                </div>

                <div v-if="showPlaintextWarning" class="mt-3 rounded-lg border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-900">
                    <p class="font-semibold">¡Cuidado! Texto plano sin cifrar.</p>
                    <p class="text-xs mt-1">Si la BD del cliente queda expuesta, todas las contraseñas se filtrarán en claro. Solo debes activar esto si la BD origen es un sistema antiguo que guarda passwords sin hash y no puedes migrarlo.</p>
                    <label class="flex items-start gap-2 mt-2">
                        <input type="checkbox" v-model="form.plaintextRiskAcknowledged" />
                        <span class="text-xs">Estoy de acuerdo en usar contraseñas sin cifrar y acepto el riesgo de que si la BD se filtra, todas las contraseñas quedan al descubierto.</span>
                    </label>
                </div>

                <h3 class="mt-6 mb-3 text-xs font-bold uppercase tracking-wider text-slate-500">Conexión MySQL</h3>
                <div class="grid sm:grid-cols-3 gap-4">
                    <label class="flex flex-col gap-1 sm:col-span-2">
                        <span class="text-sm font-medium text-slate-700">Host</span>
                        <input v-model="form.config.host" type="text" class="input" placeholder="mysql.intranet" />
                    </label>
                    <label class="flex flex-col gap-1">
                        <span class="text-sm font-medium text-slate-700">Puerto</span>
                        <input v-model.number="form.config.port" type="number" class="input" />
                    </label>
                    <label class="flex flex-col gap-1 sm:col-span-2">
                        <span class="text-sm font-medium text-slate-700">Base de datos</span>
                        <input v-model="form.config.database" type="text" class="input" placeholder="intranet" />
                    </label>
                    <label class="flex flex-col gap-1">
                        <span class="text-sm font-medium text-slate-700">TLS</span>
                        <select v-model.number="form.config.tlsMode" class="input">
                            <option v-for="o in TLS_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                        </select>
                    </label>
                </div>

                <h3 class="mt-6 mb-3 text-xs font-bold uppercase tracking-wider text-slate-500">Credenciales del usuario MySQL (cifradas en disco)</h3>
                <div class="grid sm:grid-cols-2 gap-4">
                    <label class="flex flex-col gap-1">
                        <span class="text-sm font-medium text-slate-700">Usuario</span>
                        <input v-model="form.secrets.user" type="text" class="input" :placeholder="!isNew ? '(sin cambios)' : ''" autocomplete="off" />
                    </label>
                    <label class="flex flex-col gap-1">
                        <span class="text-sm font-medium text-slate-700">Contraseña</span>
                        <input v-model="form.secrets.password" type="password" class="input" :placeholder="!isNew ? '(sin cambios)' : ''" autocomplete="off" />
                    </label>
                </div>

                <h3 class="mt-6 mb-3 text-xs font-bold uppercase tracking-wider text-slate-500">Mapeo de columnas</h3>
                <div class="grid sm:grid-cols-2 gap-4">
                    <label class="flex flex-col gap-1">
                        <span class="text-sm font-medium text-slate-700">Tabla</span>
                        <input v-model="form.config.table" type="text" class="input" />
                    </label>
                    <label class="flex flex-col gap-1">
                        <span class="text-sm font-medium text-slate-700">Columna username</span>
                        <input v-model="form.config.usernameColumn" type="text" class="input" />
                    </label>
                    <label class="flex flex-col gap-1">
                        <span class="text-sm font-medium text-slate-700">Columna password_hash</span>
                        <input v-model="form.config.passwordColumn" type="text" class="input" />
                    </label>
                    <label class="flex flex-col gap-1">
                        <span class="text-sm font-medium text-slate-700">Columna external_id (opcional)</span>
                        <input v-model="form.config.externalIdColumn" type="text" class="input" placeholder="id" />
                    </label>
                    <label class="flex flex-col gap-1">
                        <span class="text-sm font-medium text-slate-700">Columna nombre completo (opcional)</span>
                        <input v-model="form.config.fullNameColumn" type="text" class="input" placeholder="full_name" />
                    </label>
                    <label class="flex flex-col gap-1">
                        <span class="text-sm font-medium text-slate-700">Columna email (opcional)</span>
                        <input v-model="form.config.emailColumn" type="text" class="input" placeholder="email" />
                    </label>
                    <label class="flex flex-col gap-1 sm:col-span-2">
                        <span class="text-sm font-medium text-slate-700">Filtro WHERE adicional (opcional)</span>
                        <input v-model="form.config.extraWhere" type="text" class="input font-mono" placeholder="is_active = 1" />
                    </label>
                </div>

                <label class="flex items-center gap-2 mt-4">
                    <input type="checkbox" v-model="form.config.autoProvision" />
                    <span class="text-sm">Crear el usuario en la base local al primer login (auto-provisión)</span>
                </label>

                <h3 class="mt-6 mb-3 text-xs font-bold uppercase tracking-wider text-slate-500">Probar conexión</h3>
                <div class="flex gap-2 items-end">
                    <label class="flex flex-col gap-1 flex-1">
                        <span class="text-sm font-medium text-slate-700">Usuario de prueba (opcional)</span>
                        <input v-model="testProbeUsername" type="text" class="input" placeholder="empleado_prueba" />
                    </label>
                    <button type="button" class="btn btn-secondary" @click="testConnection"><Plug class="w-4 h-4" /> Probar</button>
                </div>
                <p v-if="testResult" class="mt-2 text-sm" :class="testResult.connected ? 'text-emerald-700' : 'text-red-700'">
                    <span v-if="testResult.connected">Conexión OK.</span>
                    <span v-else>Fallo de conexión: {{ testResult.detail }}</span>
                    <span v-if="testResult.userFound === true"> Usuario encontrado.</span>
                    <span v-else-if="testResult.userFound === false"> Usuario no existe en la tabla.</span>
                </p>

                <div class="mt-6 flex gap-2 justify-end">
                    <button type="button" class="btn btn-secondary" @click="closeEditor">Cancelar</button>
                    <button type="button" class="btn btn-primary" :disabled="submitting" @click="save">
                        <Save class="w-4 h-4" />
                        {{ submitting ? "Guardando…" : "Guardar" }}
                    </button>
                </div>
            </section>
        </div>
    </div>
</template>
