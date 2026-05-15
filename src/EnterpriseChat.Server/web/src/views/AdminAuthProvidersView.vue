<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRouter } from "vue-router";
import { ArrowLeft, ShieldCheck, AlertTriangle, Plus, Pencil, Trash2, Plug, Save, Database, KeyRound, Columns, Download } from "lucide-vue-next";
import { api } from "@/api/client";
import { useAuthStore } from "@/stores/auth";
import { useDialogsStore } from "@/stores/dialogs";
import { dialogError, dialogSuccess } from "@/dialogs";

/**
 * Gestión de proveedores de autenticación externos.
 *
 * Flujo en 3 pasos:
 *   1. Datos generales (nombre, prioridad, algoritmo del hash).
 *   2. Conexión al MySQL (host, BD, credenciales, TLS) → botón
 *      "Conectar y descubrir esquema" introspecta tablas reales.
 *   3. Mapeo de columnas: selects rellenados con datos del esquema, no
 *      inputs libres.
 *
 * El admin local SIEMPRE va contra SQLite local. Lo que se configura
 * aquí afecta sólo a los demás usuarios.
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

// Estado del wizard de introspección.
const introspectStatus = ref<"idle" | "loading" | "ok" | "error">("idle");
const introspectError = ref<string | null>(null);
const availableTables = ref<string[]>([]);
const availableColumns = ref<string[]>([]);
const columnsLoading = ref(false);

const testResult = ref<{ connected: boolean; userFound: boolean | null; detail: string | null } | null>(null);
const testProbeUsername = ref("");
const submitting = ref(false);

const showPlaintextWarning = computed(() => form.value.hashAlgorithm === "Plaintext");
const mappingReady = computed(() => introspectStatus.value === "ok" && availableTables.value.length > 0);

function blankConfig(): MySqlConfig {
    return {
        host: "",
        port: 3306,
        database: "",
        table: "",
        usernameColumn: "",
        passwordColumn: "",
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
    resetWizard();
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
        secrets: blankSecrets(),
    };
    resetWizard();
    // Auto-conectamos con las credenciales guardadas para que las
    // columnas se carguen sin que el admin tenga que pulsar nada.
    // Si falla (cred caducada, IP nueva), aparece el botón "Conectar".
    void connectAndDiscover();
}

function resetWizard(): void {
    introspectStatus.value = "idle";
    introspectError.value = null;
    availableTables.value = [];
    availableColumns.value = [];
    testResult.value = null;
}

function closeEditor(): void {
    isNew.value = false;
    editing.value = null;
    resetWizard();
}

async function connectAndDiscover(): Promise<void> {
    introspectStatus.value = "loading";
    introspectError.value = null;
    availableTables.value = [];
    availableColumns.value = [];
    try {
        // Editando un provider existente sin nuevas credenciales: que
        // el server use los secretos guardados (no los exponemos al
        // navegador). Si el admin cambió user/pass, los mandamos.
        const url = editing.value
            ? `/admin/auth-providers/${editing.value.id}/introspect`
            : "/admin/auth-providers/introspect";
        const { data } = await api.post<{ tables: string[] | null; columns: string[] | null }>(
            url,
            { kind: "Mysql", config: form.value.config, secrets: form.value.secrets, table: null });
        availableTables.value = data.tables ?? [];
        if (availableTables.value.length === 0) {
            introspectStatus.value = "error";
            introspectError.value = "Conexión OK pero la base de datos no contiene tablas.";
            return;
        }
        introspectStatus.value = "ok";
        // Si la tabla previa ya no existe, vaciar selección.
        if (form.value.config.table && !availableTables.value.includes(form.value.config.table)) {
            form.value.config.table = "";
        }
        if (form.value.config.table) {
            await loadColumns(form.value.config.table);
        }
    } catch (err: unknown) {
        introspectStatus.value = "error";
        const e = err as { response?: { data?: { error?: string } }, message?: string };
        introspectError.value = e.response?.data?.error ?? e.message ?? String(err);
    }
}

async function loadColumns(table: string): Promise<void> {
    if (!table) {
        availableColumns.value = [];
        return;
    }
    columnsLoading.value = true;
    try {
        const url = editing.value
            ? `/admin/auth-providers/${editing.value.id}/introspect`
            : "/admin/auth-providers/introspect";
        const { data } = await api.post<{ tables: string[] | null; columns: string[] | null }>(
            url,
            { kind: "Mysql", config: { ...form.value.config, table }, secrets: form.value.secrets, table });
        availableColumns.value = data.columns ?? [];

        // Heurística amistosa: pre-seleccionar columnas frecuentes si
        // el admin aún no las ha tocado.
        const cols = availableColumns.value;
        function preselect(current: string, candidates: string[]): string {
            if (current && cols.includes(current)) return current;
            for (const c of candidates) if (cols.includes(c)) return c;
            return current;
        }
        const cfg = form.value.config;
        cfg.usernameColumn = preselect(cfg.usernameColumn, ["username", "user", "login", "email", "name"]);
        cfg.passwordColumn = preselect(cfg.passwordColumn, ["password_hash", "password", "passwd", "pwd"]);
        if (cfg.externalIdColumn !== null) {
            cfg.externalIdColumn = preselect(cfg.externalIdColumn, ["id", "user_id", "external_id", "uuid"]);
        }
        if (cfg.fullNameColumn !== null) {
            cfg.fullNameColumn = preselect(cfg.fullNameColumn, ["full_name", "name", "display_name"]);
        }
        if (cfg.emailColumn !== null) {
            cfg.emailColumn = preselect(cfg.emailColumn, ["email", "mail"]);
        }
    } catch (err: unknown) {
        const e = err as { response?: { data?: { error?: string } }, message?: string };
        await dialogError("No se pudieron leer las columnas", e.response?.data?.error ?? e.message ?? String(err));
    } finally {
        columnsLoading.value = false;
    }
}

watch(() => form.value.config.table, async (newTable, oldTable) => {
    if (introspectStatus.value === "ok" && newTable && newTable !== oldTable) {
        await loadColumns(newTable);
    }
});

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
    // Tres opciones para los usuarios provisionados por este provider:
    //   "deactivate" → SetNull + IsActive=false. Recomendado.
    //   "keep"       → dejar locales huérfanos.
    //   "cascade"    → borrar (falla si tienen mensajes).
    const dialogs = useDialogsStore();
    const choice = await dialogs.pickOption({
        title: `Eliminar "${row.displayName}"`,
        text: "¿Qué hacemos con los usuarios que este proveedor había creado en local?",
        icon: "warning",
        defaultValue: "deactivate",
        confirmText: "Eliminar proveedor",
        options: [
            { value: "deactivate", label: "Desactivar (recomendado)", description: "Los usuarios quedan inactivos. No pueden entrar al chat. Reversible." },
            { value: "keep",       label: "Mantener activos (huérfanos)", description: "Quedan como locales sin password válida — el admin tendría que dárselo a mano." },
            { value: "cascade",    label: "Borrar definitivamente", description: "Solo funciona si NO han enviado mensajes todavía. Si los tienen, se aborta.", danger: true },
        ],
    });
    if (!choice) return;
    try {
        await api.delete(`/admin/auth-providers/${row.id}`, {
            params: { onProvisionedUsers: choice },
        });
        await refresh();
        await dialogSuccess("Proveedor eliminado");
    } catch (err: unknown) {
        const e = err as { response?: { data?: { error?: string } }, message?: string };
        await dialogError("No se pudo eliminar", e.response?.data?.error ?? e.message ?? String(err));
    }
}

function goImport(row: AuthProviderSummary): void {
    void router.push({ name: "admin-auth-provider-import", params: { id: String(row.id) } });
}

async function testConnection(): Promise<void> {
    testResult.value = null;
    try {
        // Si está editando y no ha tocado las credenciales, pasamos
        // los secretos guardados aprovechando que el endpoint /test
        // siempre requiere secrets en body. Detecto vacío y reutilizo
        // los guardados via un round-trip por el endpoint /{id}/introspect
        // que sí los descifra. Para simplicidad: si edit + no secrets,
        // invocamos primero introspect (cuyo /test inline no tenemos) →
        // mejor: enriquecer el endpoint /test para que también acepte
        // providerId; por ahora obligamos a teclear contraseña en edit
        // para correr el test de credenciales reales.
        if (editing.value && !form.value.secrets.user && !form.value.secrets.password) {
            await dialogError("Introduce las credenciales", "Para probar credenciales reales necesitas teclear usuario y contraseña — no se devuelven al editar por seguridad.");
            return;
        }
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

// Helpers de columnas opcionales: null = "ninguna".
function toggleOptional(field: "externalIdColumn" | "fullNameColumn" | "emailColumn"): void {
    const cfg = form.value.config;
    if (cfg[field] === null) cfg[field] = "";
    else cfg[field] = null;
}
</script>

<template>
    <div class="bg-slate-50 px-3 sm:px-4 py-3 sm:py-4">
        <div class="w-full">
            <button type="button" @click="router.back()" class="inline-flex items-center gap-1.5 text-sm text-slate-600 hover:text-slate-900 mb-3 sm:mb-4">
                <ArrowLeft class="w-4 h-4" />
                Volver
            </button>

            <header class="mb-4">
                <h1 class="text-xl sm:text-2xl font-bold text-slate-900 flex items-center gap-2">
                    <ShieldCheck class="w-5 h-5 sm:w-6 sm:h-6 text-blue-600 flex-shrink-0" />
                    Autenticación externa
                </h1>
                <p class="text-xs sm:text-sm text-slate-600 mt-1">
                    Permite a tus empleados iniciar sesión con las credenciales que ya usan en tu BD existente.
                    El usuario <code class="px-1 bg-slate-100 rounded">admin</code> siempre se valida contra la base local — no se puede mover fuera.
                </p>
            </header>

            <div class="rounded-lg border border-amber-200 bg-amber-50 p-3 sm:p-4 mb-4 flex items-start gap-2 sm:gap-3 text-xs sm:text-sm text-amber-900">
                <AlertTriangle class="w-4 h-4 sm:w-5 sm:h-5 flex-shrink-0 mt-0.5" />
                <div>
                    <strong>Avisos de seguridad:</strong>
                    <ul class="list-disc list-inside mt-1 space-y-1">
                        <li>Las credenciales que pegues aquí quedan cifradas con AES-256-GCM en disco, pero la copia ofuscada está al alcance de cualquier proceso con acceso al filesystem del server.</li>
                        <li>Crea un usuario MySQL específico con permisos SELECT exclusivos sobre la tabla configurada y, si tu MySQL lo soporta, restringe por IP origen.</li>
                        <li>Considera tunelizar el tráfico por VPN si tu MySQL no expone TLS.</li>
                    </ul>
                </div>
            </div>

            <section class="card p-3 sm:p-6 bg-white mb-4" v-if="!isNew && !editing">
                <div class="flex items-center justify-between mb-3 sm:mb-4 gap-3">
                    <h2 class="text-sm font-bold uppercase tracking-wider text-slate-500">Proveedores</h2>
                    <button type="button" class="btn btn-primary text-sm flex-shrink-0" @click="openNew">
                        <Plus class="w-4 h-4" />
                        <span class="hidden sm:inline">Añadir proveedor</span>
                    </button>
                </div>

                <div v-if="loading" class="text-sm text-slate-500">Cargando…</div>
                <div v-else-if="list.length === 0" class="text-sm text-slate-500">
                    Aún no hay proveedores externos. Pulsa "Añadir" para encadenar uno.
                </div>
                <template v-else>
                    <!-- Card-view <md -->
                    <ul class="md:hidden flex flex-col gap-2">
                        <li v-for="row in list" :key="row.id" class="border border-slate-200 rounded-lg p-3 bg-white">
                            <div class="flex items-center justify-between gap-2 mb-1">
                                <span class="font-medium text-slate-900 truncate flex-1">{{ row.displayName }}</span>
                                <span :class="['px-2 py-0.5 rounded-full text-xs font-bold flex-shrink-0',
                                    row.isEnabled ? 'bg-emerald-100 text-emerald-800' : 'bg-slate-100 text-slate-600']">
                                    {{ row.isEnabled ? 'Activo' : 'Inactivo' }}
                                </span>
                            </div>
                            <div class="text-xs text-slate-500">{{ row.kind }} · {{ row.hashAlgorithm }} · prioridad {{ row.priority }}</div>
                            <div class="mt-3 flex items-stretch gap-1.5">
                                <button class="btn-mobile-row" @click="goImport(row)"><Download class="w-3.5 h-3.5" /> Importar</button>
                                <button class="btn-mobile-row" @click="openEdit(row)"><Pencil class="w-3.5 h-3.5" /> Editar</button>
                                <button class="btn-mobile-row btn-mobile-row-danger" @click="remove(row)"><Trash2 class="w-3.5 h-3.5" /> Borrar</button>
                            </div>
                        </li>
                    </ul>

                    <!-- Tabla >=md -->
                    <div class="hidden md:block overflow-x-auto">
                        <table class="w-full text-sm">
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
                                    <td class="py-3 text-right space-x-2 whitespace-nowrap">
                                        <button class="btn btn-secondary text-xs" @click="goImport(row)"><Download class="w-3.5 h-3.5" /> Importar</button>
                                        <button class="btn btn-secondary text-xs" @click="openEdit(row)"><Pencil class="w-3.5 h-3.5" /> Editar</button>
                                        <button class="btn btn-secondary text-xs text-red-700 border-red-200 hover:bg-red-50" @click="remove(row)"><Trash2 class="w-3.5 h-3.5" /> Eliminar</button>
                                    </td>
                                </tr>
                            </tbody>
                        </table>
                    </div>
                </template>
            </section>

            <template v-if="isNew || editing">
                <!-- Paso 1: identidad del proveedor -->
                <section class="card p-6 bg-white mb-4">
                    <div class="flex items-center gap-2 mb-4">
                        <span class="w-7 h-7 rounded-full bg-blue-600 text-white grid place-items-center text-sm font-bold">1</span>
                        <h2 class="text-sm font-bold uppercase tracking-wider text-slate-500">Datos del proveedor</h2>
                    </div>
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
                </section>

                <!-- Paso 2: conexión -->
                <section class="card p-6 bg-white mb-4">
                    <div class="flex items-center gap-2 mb-4">
                        <span class="w-7 h-7 rounded-full bg-blue-600 text-white grid place-items-center text-sm font-bold">2</span>
                        <h2 class="text-sm font-bold uppercase tracking-wider text-slate-500">Conexión MySQL</h2>
                    </div>

                    <div class="grid sm:grid-cols-12 gap-3">
                        <label class="flex flex-col gap-1 sm:col-span-7">
                            <span class="text-sm font-medium text-slate-700">Host</span>
                            <input v-model="form.config.host" type="text" class="input" placeholder="mysql.intranet" />
                        </label>
                        <label class="flex flex-col gap-1 sm:col-span-2">
                            <span class="text-sm font-medium text-slate-700">Puerto</span>
                            <input v-model.number="form.config.port" type="number" class="input" />
                        </label>
                        <label class="flex flex-col gap-1 sm:col-span-3">
                            <span class="text-sm font-medium text-slate-700">TLS</span>
                            <select v-model.number="form.config.tlsMode" class="input">
                                <option v-for="o in TLS_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
                            </select>
                        </label>
                        <label class="flex flex-col gap-1 sm:col-span-12">
                            <span class="text-sm font-medium text-slate-700 flex items-center gap-1"><Database class="w-3.5 h-3.5" /> Base de datos</span>
                            <input v-model="form.config.database" type="text" class="input" placeholder="intranet" />
                        </label>
                        <label class="flex flex-col gap-1 sm:col-span-6">
                            <span class="text-sm font-medium text-slate-700 flex items-center gap-1"><KeyRound class="w-3.5 h-3.5" /> Usuario MySQL</span>
                            <input v-model="form.secrets.user" type="text" class="input" :placeholder="!isNew ? '(sin cambios)' : ''" autocomplete="off" />
                        </label>
                        <label class="flex flex-col gap-1 sm:col-span-6">
                            <span class="text-sm font-medium text-slate-700">Contraseña MySQL</span>
                            <input v-model="form.secrets.password" type="password" class="input" :placeholder="!isNew ? '(sin cambios)' : ''" autocomplete="off" />
                        </label>
                    </div>

                    <div class="mt-4 flex items-center gap-3">
                        <button type="button" class="btn btn-primary" :disabled="introspectStatus === 'loading'" @click="connectAndDiscover">
                            <Plug class="w-4 h-4" />
                            {{ introspectStatus === 'loading' ? 'Conectando…' : 'Conectar y descubrir esquema' }}
                        </button>
                        <span v-if="introspectStatus === 'ok'" class="text-sm text-emerald-700 flex items-center gap-1.5">
                            <ShieldCheck class="w-4 h-4" />
                            Conectado · {{ availableTables.length }} tablas detectadas
                        </span>
                        <span v-if="introspectStatus === 'error'" class="text-sm text-red-700">
                            {{ introspectError }}
                        </span>
                    </div>
                </section>

                <!-- Paso 3: mapeo de columnas (selects dinámicos) -->
                <section class="card p-6 bg-white mb-4" :class="!mappingReady ? 'opacity-60 pointer-events-none' : ''">
                    <div class="flex items-center gap-2 mb-4">
                        <span class="w-7 h-7 rounded-full bg-blue-600 text-white grid place-items-center text-sm font-bold">3</span>
                        <h2 class="text-sm font-bold uppercase tracking-wider text-slate-500 flex items-center gap-1.5">
                            <Columns class="w-4 h-4" /> Mapeo de columnas
                        </h2>
                    </div>
                    <p v-if="!mappingReady" class="text-xs text-slate-500 italic mb-3">
                        Conéctate al MySQL en el paso 2 para listar las tablas disponibles.
                    </p>

                    <div class="grid sm:grid-cols-2 gap-4">
                        <label class="flex flex-col gap-1">
                            <span class="text-sm font-medium text-slate-700">Tabla de usuarios</span>
                            <select v-model="form.config.table" class="input">
                                <option value="" disabled>Selecciona una tabla…</option>
                                <option v-for="t in availableTables" :key="t" :value="t">{{ t }}</option>
                            </select>
                        </label>
                        <div class="flex items-end text-xs text-slate-500">
                            <span v-if="columnsLoading">Leyendo columnas…</span>
                            <span v-else-if="availableColumns.length > 0">{{ availableColumns.length }} columnas detectadas en la tabla.</span>
                        </div>

                        <label class="flex flex-col gap-1">
                            <span class="text-sm font-medium text-slate-700">Columna con el username / email de login</span>
                            <select v-model="form.config.usernameColumn" class="input" :disabled="availableColumns.length === 0">
                                <option value="" disabled>—</option>
                                <option v-for="c in availableColumns" :key="c" :value="c">{{ c }}</option>
                            </select>
                        </label>
                        <label class="flex flex-col gap-1">
                            <span class="text-sm font-medium text-slate-700">Columna con el hash / contraseña</span>
                            <select v-model="form.config.passwordColumn" class="input" :disabled="availableColumns.length === 0">
                                <option value="" disabled>—</option>
                                <option v-for="c in availableColumns" :key="c" :value="c">{{ c }}</option>
                            </select>
                        </label>

                        <div class="sm:col-span-2 border-t border-slate-100 pt-3 mt-1">
                            <p class="text-xs font-semibold uppercase tracking-wider text-slate-500 mb-2">Columnas opcionales (mejoran el perfil del usuario auto-provisionado)</p>

                            <div class="grid sm:grid-cols-3 gap-4">
                                <div>
                                    <label class="text-sm font-medium text-slate-700 flex items-center gap-2">
                                        <input type="checkbox" :checked="form.config.externalIdColumn !== null" @change="toggleOptional('externalIdColumn')" />
                                        External ID
                                    </label>
                                    <select v-if="form.config.externalIdColumn !== null" v-model="form.config.externalIdColumn" class="input mt-1" :disabled="availableColumns.length === 0">
                                        <option value="" disabled>—</option>
                                        <option v-for="c in availableColumns" :key="c" :value="c">{{ c }}</option>
                                    </select>
                                </div>
                                <div>
                                    <label class="text-sm font-medium text-slate-700 flex items-center gap-2">
                                        <input type="checkbox" :checked="form.config.fullNameColumn !== null" @change="toggleOptional('fullNameColumn')" />
                                        Nombre completo
                                    </label>
                                    <select v-if="form.config.fullNameColumn !== null" v-model="form.config.fullNameColumn" class="input mt-1" :disabled="availableColumns.length === 0">
                                        <option value="" disabled>—</option>
                                        <option v-for="c in availableColumns" :key="c" :value="c">{{ c }}</option>
                                    </select>
                                </div>
                                <div>
                                    <label class="text-sm font-medium text-slate-700 flex items-center gap-2">
                                        <input type="checkbox" :checked="form.config.emailColumn !== null" @change="toggleOptional('emailColumn')" />
                                        Email
                                    </label>
                                    <select v-if="form.config.emailColumn !== null" v-model="form.config.emailColumn" class="input mt-1" :disabled="availableColumns.length === 0">
                                        <option value="" disabled>—</option>
                                        <option v-for="c in availableColumns" :key="c" :value="c">{{ c }}</option>
                                    </select>
                                </div>
                            </div>
                        </div>

                        <label class="flex flex-col gap-1 sm:col-span-2">
                            <span class="text-sm font-medium text-slate-700">Filtro WHERE adicional (opcional)</span>
                            <input v-model="form.config.extraWhere" type="text" class="input font-mono" placeholder="is_active = 1" />
                            <span class="text-xs text-slate-500">Solo comparaciones simples. No se admiten <code>;</code>, comentarios ni subconsultas.</span>
                        </label>
                    </div>

                    <label class="flex items-center gap-2 mt-4">
                        <input type="checkbox" v-model="form.config.autoProvision" />
                        <span class="text-sm">Crear el usuario en la base local al primer login (auto-provisión)</span>
                    </label>
                </section>

                <!-- Probar credenciales reales -->
                <section class="card p-6 bg-white mb-4" :class="!mappingReady ? 'opacity-60 pointer-events-none' : ''">
                    <h2 class="text-sm font-bold uppercase tracking-wider text-slate-500 mb-3">Probar usuario real (opcional)</h2>
                    <p class="text-xs text-slate-500 mb-2">Sin contraseña: solo comprueba si la fila existe. Útil para validar el mapeo.</p>
                    <div class="flex gap-2 items-end">
                        <label class="flex flex-col gap-1 flex-1">
                            <span class="text-sm font-medium text-slate-700">Username/email del MySQL externo</span>
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
                </section>

                <div class="flex gap-2 justify-end pb-8">
                    <button type="button" class="btn btn-secondary" @click="closeEditor">Cancelar</button>
                    <button type="button" class="btn btn-primary" :disabled="submitting" @click="save">
                        <Save class="w-4 h-4" />
                        {{ submitting ? "Guardando…" : "Guardar" }}
                    </button>
                </div>
            </template>
        </div>
    </div>
</template>
