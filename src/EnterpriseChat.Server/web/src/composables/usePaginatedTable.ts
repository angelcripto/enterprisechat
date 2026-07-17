import { computed, reactive, ref, watch } from "vue";

/**
 * Composable de estado para una tabla con paginación server-side,
 * búsqueda con debounce, ordenación por columna y selección
 * cross-page (estilo Mailchimp).
 *
 * El `loader` lo provee el call site: recibe los parámetros y devuelve
 * `{ rows, total }` desde su API. El `allIdsLoader` es opcional y
 * habilita el modo "seleccionar todos los del filtro" — si se omite,
 * la tabla mantiene la selección a lo visible.
 */

export type SortDir = "asc" | "desc";

export interface LoaderParams {
    page: number;
    pageSize: number;
    search: string;
    sort: string | null;
    dir: SortDir;
}

export interface PaginatedLoader<TRow> {
    (params: LoaderParams): Promise<{ rows: TRow[]; total: number }>;
}

export interface AllIdsLoader<TRowKey extends number | string> {
    (params: Omit<LoaderParams, "page" | "pageSize">): Promise<{ ids: TRowKey[]; truncatedAt?: number }>;
}

export interface UsePaginatedTableOptions<TRow, TRowKey extends number | string> {
    loader: PaginatedLoader<TRow>;
    allIdsLoader?: AllIdsLoader<TRowKey>;
    rowKey: (r: TRow) => TRowKey;
    defaultSort?: string | null;
    defaultDir?: SortDir;
    pageSize?: number;
    searchDebounceMs?: number;
}

export type SelectionMode = "none" | "visible" | "all-matching";

export function usePaginatedTable<TRow, TRowKey extends number | string = number>(
    options: UsePaginatedTableOptions<TRow, TRowKey>,
) {
    const {
        loader,
        allIdsLoader,
        rowKey,
        defaultSort = null,
        defaultDir = "asc",
        pageSize: defaultPageSize = 50,
        searchDebounceMs = 350,
    } = options;

    const rows = ref<TRow[]>([]) as { value: TRow[] };
    const total = ref(0);
    const page = ref(0);
    const pageSize = ref(defaultPageSize);
    const sort = ref<string | null>(defaultSort);
    const dir = ref<SortDir>(defaultDir);
    const search = ref("");
    const loading = ref(false);
    const lastError = ref<string | null>(null);

    // Selección cross-page guardada en un Set para O(1) lookup.
    // `reactive()` reescribe los tipos que envuelve (UnwrapRefSimple). Con TRowKey aún sin
    // resolver, TypeScript no puede probar que TRowKey sea asignable a su propia versión
    // desenvuelta, y falla en cada .add()/.has()/.delete(). El cast conserva la
    // reactividad real del Set y devuelve el tipo que espera el resto del código.
    const selected = reactive(new Set<TRowKey>()) as Set<TRowKey>;
    const selectionMode = ref<SelectionMode>("none");

    const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)));
    const selectedCount = computed(() => selected.size);

    let inflight = 0;
    async function reload(): Promise<void> {
        const ticket = ++inflight;
        loading.value = true;
        lastError.value = null;
        try {
            const result = await loader({
                page: page.value,
                pageSize: pageSize.value,
                search: search.value,
                sort: sort.value,
                dir: dir.value,
            });
            // Ignora respuestas viejas (el usuario tecleó rápido).
            if (ticket !== inflight) return;
            rows.value = result.rows;
            total.value = result.total;
        } catch (err) {
            if (ticket !== inflight) return;
            lastError.value = err instanceof Error ? err.message : String(err);
        } finally {
            if (ticket === inflight) loading.value = false;
        }
    }

    function changePage(newPage: number): void {
        if (newPage < 0 || newPage >= totalPages.value) return;
        page.value = newPage;
        void reload();
    }

    function setSort(col: string): void {
        if (sort.value === col) {
            dir.value = dir.value === "asc" ? "desc" : "asc";
        } else {
            sort.value = col;
            dir.value = "asc";
        }
        page.value = 0;
        void reload();
    }

    // Debounce del search: evitamos un request por tecla.
    let searchTimer = 0;
    function setSearch(value: string): void {
        search.value = value;
        clearSelection();
        window.clearTimeout(searchTimer);
        searchTimer = window.setTimeout(() => {
            page.value = 0;
            void reload();
        }, searchDebounceMs);
    }

    function toggleRow(row: TRow): boolean {
        const key = rowKey(row);
        if (selected.has(key)) {
            selected.delete(key);
            if (selected.size === 0) selectionMode.value = "none";
            else if (selectionMode.value === "all-matching") selectionMode.value = "visible";
            return false;
        }
        selected.add(key);
        if (selectionMode.value === "none") selectionMode.value = "visible";
        return true;
    }

    function toggleAllVisible(): void {
        const visible = rows.value.map(rowKey);
        const allMarked = visible.every((k) => selected.has(k));
        if (allMarked) {
            for (const k of visible) selected.delete(k);
            if (selected.size === 0) selectionMode.value = "none";
        } else {
            for (const k of visible) selected.add(k);
            selectionMode.value = "visible";
        }
    }

    async function selectAllMatching(): Promise<{ ok: boolean; truncatedAt?: number; error?: string }> {
        if (!allIdsLoader) return { ok: false, error: "allIdsLoader no configurado." };
        try {
            const { ids, truncatedAt } = await allIdsLoader({
                search: search.value,
                sort: sort.value,
                dir: dir.value,
            });
            selected.clear();
            for (const id of ids) selected.add(id);
            selectionMode.value = "all-matching";
            return { ok: true, truncatedAt };
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            return { ok: false, error: msg };
        }
    }

    function clearSelection(): void {
        selected.clear();
        selectionMode.value = "none";
    }

    // Recarga inicial cuando el composable se monta dentro de un setup.
    // Se hace explícita: el caller llama `reload()` en `onMounted` para
    // poder esperar / encadenar `Promise.all` con otras cargas.

    watch([page, pageSize], () => { /* triggered manualmente desde changePage */ });

    return {
        // estado lectura
        rows, total, page, pageSize, sort, dir, search, loading, lastError,
        selected, selectionMode, selectedCount, totalPages,
        // acciones
        reload, changePage, setSort, setSearch,
        toggleRow, toggleAllVisible, selectAllMatching, clearSelection,
    };
}
