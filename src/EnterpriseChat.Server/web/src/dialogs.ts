import Swal, { type SweetAlertIcon, type SweetAlertResult } from "sweetalert2";
import "sweetalert2/dist/sweetalert2.min.css";

/**
 * Thin wrapper around SweetAlert2 with the EnterpriseChat colour palette
 * baked in so callers don't repeat themselves. Every dialog uses
 * heightInherit + transparent backdrop so it sits cleanly over the chat UI.
 */
const themed = Swal.mixin({
    background: "#ffffff",
    color: "#0f172a",
    confirmButtonColor: "#1d4ed8",
    cancelButtonColor: "#94a3b8",
    customClass: {
        popup: "rounded-2xl shadow-xl",
        title: "text-slate-900 font-semibold text-lg",
        htmlContainer: "text-slate-700",
        confirmButton: "rounded-lg px-4 py-2 font-medium",
        cancelButton: "rounded-lg px-4 py-2 font-medium",
        input: "rounded-lg border border-slate-200",
    },
    buttonsStyling: false,
});

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
    const result = await themed.fire({
        title: opts.title,
        text: opts.text,
        input: opts.inputType ?? "text",
        inputPlaceholder: opts.placeholder,
        inputValue: opts.initial ?? "",
        showCancelButton: true,
        confirmButtonText: opts.confirmText ?? "Aceptar",
        cancelButtonText: opts.cancelText ?? "Cancelar",
        focusConfirm: false,
        inputValidator: (value: string) => {
            if (opts.validator) return opts.validator(value) ?? undefined;
            return undefined;
        },
    });
    return resultToString(result);
}

export interface ConfirmOptions {
    title: string;
    text?: string;
    confirmText?: string;
    cancelText?: string;
    icon?: SweetAlertIcon;
    danger?: boolean;
}

export async function dialogConfirm(opts: ConfirmOptions): Promise<boolean> {
    const result = await themed.fire({
        title: opts.title,
        text: opts.text,
        icon: opts.icon ?? (opts.danger ? "warning" : "question"),
        showCancelButton: true,
        confirmButtonText: opts.confirmText ?? "Aceptar",
        cancelButtonText: opts.cancelText ?? "Cancelar",
        confirmButtonColor: opts.danger ? "#dc2626" : "#1d4ed8",
        reverseButtons: opts.danger === true,
    });
    return result.isConfirmed;
}

export function dialogSuccess(title: string, text?: string): Promise<SweetAlertResult> {
    return themed.fire({ title, text, icon: "success", timer: 2200, showConfirmButton: false });
}

export function dialogError(title: string, text?: string): Promise<SweetAlertResult> {
    return themed.fire({ title, text, icon: "error" });
}

export function dialogInfo(title: string, text?: string): Promise<SweetAlertResult> {
    return themed.fire({ title, text, icon: "info" });
}

function resultToString(result: SweetAlertResult): string | null {
    if (!result.isConfirmed) return null;
    const v = result.value;
    return typeof v === "string" ? v : null;
}
