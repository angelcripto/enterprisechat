import { defineStore } from "pinia";
import { ref } from "vue";

/**
 * Store imperativo para diálogos modales propios. El `<DialogHost />`
 * montado en App.vue suscribe esta cola y renderiza el modal de turno.
 *
 * Diseño:
 *   - Cola FIFO: si dos llamadas se solapan, salen una detrás de otra.
 *   - Cada diálogo expone una `Promise` resuelta al cerrarse, con el
 *     valor capturado (boolean para confirm, string|null para prompt,
 *     etc.).
 *   - El consumidor preferido es `src/dialogs.ts` (wrapper con la
 *     misma firma que la versión vieja basada en SweetAlert).
 */

export type DialogIcon = "success" | "error" | "info" | "warning" | "question" | undefined;

export interface ConfirmRequest {
    kind: "confirm";
    id: number;
    title: string;
    text?: string;
    html?: string;
    icon?: DialogIcon;
    confirmText?: string;
    cancelText?: string;
    danger?: boolean;
    resolve: (ok: boolean) => void;
}

export interface PromptRequest {
    kind: "prompt";
    id: number;
    title: string;
    label?: string;
    text?: string;
    placeholder?: string;
    inputType?: "text" | "password";
    initial?: string;
    confirmText?: string;
    cancelText?: string;
    validator?: (v: string) => string | null;
    resolve: (value: string | null) => void;
}

export interface AlertRequest {
    kind: "alert";
    id: number;
    title: string;
    text?: string;
    html?: string;
    icon: Exclude<DialogIcon, undefined>;
    autoCloseMs?: number;
    confirmText?: string;
    resolve: () => void;
}

export interface OptionItem {
    value: string;
    label: string;
    description?: string;
    danger?: boolean;
}

export interface OptionsRequest {
    kind: "options";
    id: number;
    title: string;
    text?: string;
    html?: string;
    icon?: DialogIcon;
    options: OptionItem[];
    defaultValue?: string;
    confirmText?: string;
    cancelText?: string;
    resolve: (value: string | null) => void;
}

export type DialogRequest = ConfirmRequest | PromptRequest | AlertRequest | OptionsRequest;

let counter = 0;

export const useDialogsStore = defineStore("dialogs", () => {
    const queue = ref<DialogRequest[]>([]);

    function enqueue<T extends DialogRequest>(req: T): void {
        queue.value = [...queue.value, req];
    }

    function pop(id: number): void {
        queue.value = queue.value.filter((d) => d.id !== id);
    }

    function confirm(opts: Omit<ConfirmRequest, "kind" | "id" | "resolve">): Promise<boolean> {
        return new Promise((resolve) => {
            enqueue({ kind: "confirm", id: ++counter, ...opts, resolve });
        });
    }

    function prompt(opts: Omit<PromptRequest, "kind" | "id" | "resolve">): Promise<string | null> {
        return new Promise((resolve) => {
            enqueue({ kind: "prompt", id: ++counter, ...opts, resolve });
        });
    }

    function alert(opts: Omit<AlertRequest, "kind" | "id" | "resolve">): Promise<void> {
        return new Promise((resolve) => {
            enqueue({ kind: "alert", id: ++counter, ...opts, resolve });
        });
    }

    function pickOption(opts: Omit<OptionsRequest, "kind" | "id" | "resolve">): Promise<string | null> {
        return new Promise((resolve) => {
            enqueue({ kind: "options", id: ++counter, ...opts, resolve });
        });
    }

    function success(title: string, text?: string): Promise<void> {
        return alert({ title, text, icon: "success", autoCloseMs: 2200 });
    }
    function error(title: string, text?: string): Promise<void> {
        return alert({ title, text, icon: "error" });
    }
    function info(title: string, text?: string): Promise<void> {
        return alert({ title, text, icon: "info" });
    }

    return { queue, pop, confirm, prompt, alert, pickOption, success, error, info };
});
