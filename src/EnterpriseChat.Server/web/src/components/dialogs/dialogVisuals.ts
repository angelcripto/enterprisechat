import { CheckCircle2, XCircle, AlertTriangle, Info, HelpCircle } from "lucide-vue-next";
import type { Component } from "vue";
import type { DialogIcon } from "@/stores/dialogs";

/**
 * Mapping centralizado de icono → componente lucide + clases Tailwind.
 * Mantiene la paleta consistente en todos los diálogos. Si quieres
 * cambiar el rojo del danger, se cambia aquí y propaga.
 */
export interface IconVisual {
    component: Component;
    color: string;       // text-{color}
    bg: string;          // bg-{color}/10 para halo
}

const map: Record<Exclude<DialogIcon, undefined>, IconVisual> = {
    success:  { component: CheckCircle2,   color: "text-emerald-600", bg: "bg-emerald-50" },
    error:    { component: XCircle,        color: "text-red-600",     bg: "bg-red-50" },
    warning:  { component: AlertTriangle,  color: "text-amber-600",   bg: "bg-amber-50" },
    info:     { component: Info,           color: "text-blue-600",    bg: "bg-blue-50" },
    question: { component: HelpCircle,     color: "text-slate-600",   bg: "bg-slate-100" },
};

export function visualFor(icon: DialogIcon): IconVisual | null {
    if (!icon) return null;
    return map[icon];
}
