import { driver } from "driver.js";
import "driver.js/dist/driver.css";

/**
 * Guided tour por las piezas principales del chat. Se invoca desde el botón
 * de ayuda en la TopBar. Cada step apunta a un selector data-tour="..."
 * colocado en los componentes correspondientes.
 *
 * El usuario puede pulsar Esc o el aspa para salir, navegar con flechas
 * Enter, y revisitar el tour cuando quiera desde el mismo botón.
 */
export function startGuidedTour(): void {
    const d = driver({
        showProgress: true,
        nextBtnText: "Siguiente",
        prevBtnText: "Anterior",
        doneBtnText: "Listo",
        progressText: "{{current}} de {{total}}",
        steps: [
            {
                element: '[data-tour="workspace"]',
                popover: {
                    title: "Tu workspace",
                    description: "Aquí ves el nombre de la empresa y tu plan actual. El menú abre acciones de administración: gestionar licencia, invitar personas y cerrar sesión.",
                    side: "right",
                    align: "start",
                },
            },
            {
                element: '[data-tour="sections"]',
                popover: {
                    title: "Inicio · Menciones · Borradores · Guardados",
                    description: "Vistas para no perder nada: la actividad reciente, los mensajes donde te mencionan con @, lo que dejaste a medias y lo que marcaste para volver.",
                    side: "right",
                },
            },
            {
                element: '[data-tour="channels"]',
                popover: {
                    title: "Canales",
                    description: "Conversaciones por tema, departamento o proyecto. Pulsa + para crear uno nuevo, público o privado.",
                    side: "right",
                },
            },
            {
                element: '[data-tour="dms"]',
                popover: {
                    title: "Mensajes directos",
                    description: "Conversaciones 1 a 1 con tus compañeros. Si hay un número junto al nombre, son mensajes no leídos.",
                    side: "right",
                },
            },
            {
                element: '[data-tour="search"]',
                popover: {
                    title: "Búsqueda",
                    description: "Encuentra mensajes antiguos por palabra, autor o sala. Sin salir del navegador.",
                    side: "bottom",
                },
            },
            {
                element: '[data-tour="profile"]',
                popover: {
                    title: "Tu perfil",
                    description: "Cambia tu foto, gestiona tu sesión y cierra cuando termines.",
                    side: "bottom",
                    align: "end",
                },
            },
            {
                element: '[data-tour="composer"]',
                popover: {
                    title: "Escribir y adjuntar",
                    description: "Escribe en la barra inferior. Adjunta archivos con el clip. Pulsa Enter para enviar; Shift+Enter para salto de línea.",
                    side: "top",
                },
            },
            {
                element: '[data-tour="rightpanel"]',
                popover: {
                    title: "Panel de detalles",
                    description: "Información de la conversación actual: miembros, archivos compartidos y el estado del servidor (siempre tuyo).",
                    side: "left",
                },
            },
            {
                popover: {
                    title: "¡Listo!",
                    description: "Puedes lanzar este tour cuantas veces quieras desde el botón de ayuda en la barra superior.",
                },
            },
        ],
    });
    d.drive();
}
