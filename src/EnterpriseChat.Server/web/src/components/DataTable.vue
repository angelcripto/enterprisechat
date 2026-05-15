<script setup lang="ts" generic="TRow, TRowKey extends number | string">
import { computed, useSlots } from "vue";
import { ChevronUp, ChevronDown, ChevronsUpDown, Search, Loader2, ChevronLeft, ChevronRight } from "lucide-vue-next";
import type { usePaginatedTable } from "@/composables/usePaginatedTable";

/**
 * Tabla paginada server-side con sort, search debounced y selección
 * cross-page. En viewports <md presenta los resultados como cards
 * apiladas (slot `mobile-card`) en lugar de tabla, para que los
 * botones sean tactiles y el contenido no desborde.
 */

interface Column<TKey extends string = string> {
    key: TKey;
    label: string;
    sortable?: boolean;
    width?: string;
    align?: "left" | "right" | "center";
    /**
     * Oculta esta columna en viewports por debajo del breakpoint dado.
     * El header y las celdas se sincronizan con la misma clase.
     */
    hideUntil?: "sm" | "md" | "lg" | "xl";
}

const props = defineProps<{
    columns: Column[];
    controller: ReturnType<typeof usePaginatedTable<TRow, TRowKey>>;
    rowKey: (row: TRow) => TRowKey;
    selectable?: boolean;
    rowDisabled?: (row: TRow) => boolean;
    placeholderSearch?: string;
    allowSelectAllMatching?: boolean;
}>();

defineSlots<{
    [key: `cell-${string}`]: (props: { row: TRow }) => unknown;
    "row-actions"?: (props: { row: TRow }) => unknown;
    "bulk-actions"?: (props: { count: number; mode: string }) => unknown;
    "mobile-card"?: (props: { row: TRow }) => unknown;
    empty?: () => unknown;
}>();

const slots = useSlots();
const c = props.controller;
const hasMobileCard = computed(() => Boolean(slots["mobile-card"]));

const visibleSelectedCount = computed(() =>
    c.rows.value.filter((r) => c.selected.has(props.rowKey(r))).length);
const allVisibleSelected = computed(() => {
    const visibles = c.rows.value;
    return visibles.length > 0 && visibles.every((r) => c.selected.has(props.rowKey(r)));
});
const indeterminate = computed(() => visibleSelectedCount.value > 0 && !allVisibleSelected.value);

function sortIcon(col: Column) {
    if (!col.sortable) return null;
    if (c.sort.value !== col.key) return ChevronsUpDown;
    return c.dir.value === "asc" ? ChevronUp : ChevronDown;
}

function hideClass(col: Column): string {
    if (!col.hideUntil) return "";
    switch (col.hideUntil) {
        case "sm": return "hidden sm:table-cell";
        case "md": return "hidden md:table-cell";
        case "lg": return "hidden lg:table-cell";
        case "xl": return "hidden xl:table-cell";
    }
}

function onSearchInput(ev: Event): void {
    c.setSearch((ev.target as HTMLInputElement).value);
}

async function onSelectAllMatching(): Promise<void> {
    await c.selectAllMatching();
}
</script>

<template>
    <section class="bg-white overflow-hidden border-y border-slate-200">
        <header class="flex flex-wrap items-center gap-2 p-3 border-b border-slate-100">
            <div class="flex items-center gap-2 flex-1 min-w-[180px] max-w-md">
                <Search class="w-4 h-4 text-slate-400 flex-shrink-0" />
                <input
                    type="text"
                    class="input flex-1 border-0 focus:ring-0 focus:border-0"
                    :placeholder="placeholderSearch ?? 'Buscar…'"
                    :value="c.search.value"
                    @input="onSearchInput"
                />
                <Loader2 v-if="c.loading.value" class="w-4 h-4 text-slate-400 animate-spin" />
            </div>
            <div v-if="selectable && c.selectedCount.value > 0" class="flex items-center gap-2 text-sm flex-wrap">
                <span class="text-slate-700"><strong>{{ c.selectedCount.value }}</strong> seleccionados</span>
                <button type="button" class="btn-ghost text-xs px-2 py-1 rounded" @click="c.clearSelection()">Limpiar</button>
                <slot name="bulk-actions" :count="c.selectedCount.value" :mode="c.selectionMode.value" />
            </div>
        </header>

        <div
            v-if="selectable && allowSelectAllMatching && c.selectionMode.value === 'visible' && c.total.value > c.rows.value.length"
            class="px-4 py-2 bg-blue-50 border-b border-blue-100 text-sm text-blue-900 flex flex-wrap items-center justify-between gap-3"
        >
            <span>
                Has marcado los <strong>{{ visibleSelectedCount }}</strong> de esta página.
                ¿Seleccionar los <strong>{{ c.total.value }}</strong> que cumplen el filtro?
            </span>
            <button type="button" class="btn btn-secondary text-xs" @click="onSelectAllMatching">
                Seleccionar {{ c.total.value }} en todas las páginas
            </button>
        </div>
        <div
            v-if="selectable && c.selectionMode.value === 'all-matching'"
            class="px-4 py-2 bg-blue-50 border-b border-blue-100 text-sm text-blue-900 flex flex-wrap items-center justify-between gap-3"
        >
            <span>
                Se han seleccionado <strong>{{ c.selectedCount.value }}</strong> usuarios en todas las páginas que cumplen el filtro.
            </span>
            <button type="button" class="btn-ghost text-xs px-2 py-1 rounded underline" @click="c.clearSelection()">Limpiar selección</button>
        </div>

        <!-- Vista móvil: cards apiladas. Solo si el call site provee el slot. -->
        <div v-if="hasMobileCard" class="md:hidden">
            <div v-if="selectable && c.rows.value.length > 0" class="px-3 py-2 border-b border-slate-100 flex items-center gap-2 text-xs text-slate-600">
                <input
                    type="checkbox"
                    :checked="allVisibleSelected"
                    :indeterminate.prop="indeterminate"
                    @change="c.toggleAllVisible()"
                />
                <span>Seleccionar página ({{ c.rows.value.length }})</span>
            </div>
            <div v-if="c.loading.value && c.rows.value.length === 0" class="p-6 text-center text-slate-500">
                <Loader2 class="w-5 h-5 inline animate-spin" />
            </div>
            <div v-else-if="c.rows.value.length === 0" class="p-6 text-center text-sm text-slate-500">
                <slot name="empty">No hay resultados.</slot>
            </div>
            <ul v-else class="divide-y divide-slate-100">
                <li
                    v-for="row in c.rows.value"
                    :key="String(rowKey(row))"
                    class="p-3 flex gap-2"
                    :class="rowDisabled?.(row) ? 'opacity-60' : ''"
                >
                    <input
                        v-if="selectable"
                        type="checkbox"
                        class="mt-1 flex-shrink-0"
                        :checked="c.selected.has(rowKey(row))"
                        :disabled="rowDisabled?.(row) ?? false"
                        @change="c.toggleRow(row)"
                    />
                    <div class="flex-1 min-w-0">
                        <slot name="mobile-card" :row="row" />
                    </div>
                </li>
            </ul>
        </div>

        <!-- Vista desktop (siempre activa cuando NO hay slot mobile).
             overflow-x-auto siempre: si la tabla excede el ancho del
             contenedor (sidebar ancho + pantalla corta), el scroll vive
             DENTRO del card en vez de empujar la página. -->
        <div :class="['overflow-x-auto scrollbar-thin', hasMobileCard ? 'hidden md:block' : '']">
            <table class="w-full text-sm">
                <thead class="text-left text-xs uppercase tracking-wider text-slate-500 bg-slate-50">
                    <tr>
                        <th v-if="selectable" class="px-2 py-1.5 w-10">
                            <input
                                type="checkbox"
                                :checked="allVisibleSelected"
                                :indeterminate.prop="indeterminate"
                                :disabled="c.rows.value.length === 0"
                                @change="c.toggleAllVisible()"
                            />
                        </th>
                        <th
                            v-for="col in columns"
                            :key="col.key"
                            :class="['px-2 py-1.5 font-semibold whitespace-nowrap', col.align === 'right' ? 'text-right' : col.align === 'center' ? 'text-center' : '', col.sortable ? 'cursor-pointer select-none hover:text-slate-700' : '', hideClass(col)]"
                            :style="col.width ? { width: col.width } : undefined"
                            @click="col.sortable ? c.setSort(col.key) : null"
                        >
                            <span class="inline-flex items-center gap-1">
                                {{ col.label }}
                                <component v-if="col.sortable" :is="sortIcon(col)" class="w-3 h-3" />
                            </span>
                        </th>
                        <th v-if="$slots['row-actions']" class="px-2 py-1.5 text-right w-1 whitespace-nowrap data-actions-sticky">Acciones</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-if="c.loading.value && c.rows.value.length === 0">
                        <td :colspan="columns.length + (selectable ? 1 : 0) + ($slots['row-actions'] ? 1 : 0)" class="px-4 py-8 text-center text-slate-500">
                            <Loader2 class="w-5 h-5 inline animate-spin" />
                        </td>
                    </tr>
                    <tr v-else-if="c.rows.value.length === 0">
                        <td :colspan="columns.length + (selectable ? 1 : 0) + ($slots['row-actions'] ? 1 : 0)" class="px-4 py-8 text-center text-slate-500">
                            <slot name="empty">No hay resultados.</slot>
                        </td>
                    </tr>
                    <tr
                        v-for="row in c.rows.value"
                        :key="String(rowKey(row))"
                        class="border-t border-slate-100 hover:bg-slate-50"
                        :class="rowDisabled?.(row) ? 'opacity-60' : ''"
                    >
                        <td v-if="selectable" class="px-2 py-1.5">
                            <input
                                type="checkbox"
                                :checked="c.selected.has(rowKey(row))"
                                :disabled="rowDisabled?.(row) ?? false"
                                @change="c.toggleRow(row)"
                            />
                        </td>
                        <td
                            v-for="col in columns"
                            :key="col.key"
                            :class="['px-2 py-1.5', col.align === 'right' ? 'text-right' : col.align === 'center' ? 'text-center' : '', hideClass(col)]"
                        >
                            <slot :name="`cell-${col.key}`" :row="row">{{ (row as any)[col.key] ?? '—' }}</slot>
                        </td>
                        <td v-if="$slots['row-actions']" class="px-2 py-1.5 text-right data-actions-sticky">
                            <slot name="row-actions" :row="row" />
                        </td>
                    </tr>
                </tbody>
            </table>
        </div>

        <footer v-if="c.total.value > c.pageSize.value" class="flex flex-wrap items-center justify-between gap-2 px-2 py-1.5 border-t border-slate-100 text-sm text-slate-600">
            <span>{{ c.total.value }} resultados · página {{ c.page.value + 1 }} / {{ c.totalPages.value }}</span>
            <div class="flex gap-1">
                <button type="button" class="btn btn-secondary text-xs px-2 py-1" :disabled="c.page.value === 0" @click="c.changePage(c.page.value - 1)">
                    <ChevronLeft class="w-3 h-3" /> Anterior
                </button>
                <button type="button" class="btn btn-secondary text-xs px-2 py-1" :disabled="c.page.value + 1 >= c.totalPages.value" @click="c.changePage(c.page.value + 1)">
                    Siguiente <ChevronRight class="w-3 h-3" />
                </button>
            </div>
        </footer>
    </section>
</template>
