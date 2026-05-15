import { useDialogsStore, type DialogIcon } from "@/stores/dialogs";

/**
 * Capa de compatibilidad con la API anterior (basada en SweetAlert).
 * Las funciones siguen aceptando las mismas opciones y devolviendo los
 * mismos tipos, pero por debajo todo se delega a `useDialogsStore` y los
 * componentes `ConfirmDialog`, `PromptDialog`, `AlertDialog` y
 * `OptionsDialog` montados por `DialogHost`.
 *
 * Mantener este archivo nos evita tocar los ~9 call sites del chat
 * (Sidebar, TopBar, modales de canal/invitación, vistas admin) en este
 * refactor: cuando un futuro PR los migre a `useDialogsStore` directo,
 * este archivo se puede borrar.
 */

export interface PromptOptions {
    title: string;
    text?: string;
    placeholder?: string;
    inputType?: "text" | "password";
    confirmText?: string;
    cancelText?: string;
    initial?: string;
    validator?: (value: string) => string | null;
}

export async function dialogPrompt(opts: PromptOptions): Promise<string | null> {
    const store = useDialogsStore();
    return store.prompt({
        title: opts.title,
        text: opts.text,
        placeholder: opts.placeholder,
        inputType: opts.inputType,
        initial: opts.initial,
        confirmText: opts.confirmText,
        cancelText: opts.cancelText,
        validator: opts.validator,
    });
}

export interface ConfirmOptions {
    title: string;
    text?: string;
    confirmText?: string;
    cancelText?: string;
    icon?: DialogIcon;
    danger?: boolean;
}

export async function dialogConfirm(opts: ConfirmOptions): Promise<boolean> {
    const store = useDialogsStore();
    return store.confirm({
        title: opts.title,
        text: opts.text,
        icon: opts.icon ?? (opts.danger ? "warning" : "question"),
        confirmText: opts.confirmText,
        cancelText: opts.cancelText,
        danger: opts.danger,
    });
}

export function dialogSuccess(title: string, text?: string): Promise<void> {
    const store = useDialogsStore();
    return store.success(title, text);
}

export function dialogError(title: string, text?: string): Promise<void> {
    const store = useDialogsStore();
    return store.error(title, text);
}

export function dialogInfo(title: string, text?: string): Promise<void> {
    const store = useDialogsStore();
    return store.info(title, text);
}
