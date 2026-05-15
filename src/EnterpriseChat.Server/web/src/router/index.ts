import { createRouter, createWebHistory, type RouteLocationNormalized } from "vue-router";
import { useAuthStore } from "@/stores/auth";
import { setUnauthorizedHandler } from "@/api/client";

const LoginView = () => import("@/views/LoginView.vue");
const ChatView = () => import("@/views/ChatView.vue");
const AdminLayout = () => import("@/views/AdminLayout.vue");
const AdminLicenseView = () => import("@/views/AdminLicenseView.vue");
const AdminAuthProvidersView = () => import("@/views/AdminAuthProvidersView.vue");
const AdminAuthProviderImportView = () => import("@/views/AdminAuthProviderImportView.vue");
const AdminUsersView = () => import("@/views/AdminUsersView.vue");
const AdminChangePasswordView = () => import("@/views/AdminChangePasswordView.vue");

export const router = createRouter({
    history: createWebHistory(),
    routes: [
        { path: "/login", name: "login", component: LoginView, meta: { public: true } },

        // All authenticated routes are rendered inside ChatView, which owns
        // Sidebar + TopBar + RightPanel. ChatView decides which centre pane
        // component to mount based on route.name (chat, inbox, mentions,
        // drafts, saved, team) so the chrome doesn't remount between views.
        { path: "/", name: "inbox", component: ChatView },
        { path: "/channels/:roomId", name: "channel", component: ChatView, props: true },
        { path: "/dm/:peerUserId", name: "dm", component: ChatView, props: true },
        { path: "/mentions", name: "mentions", component: ChatView },
        { path: "/drafts", name: "drafts", component: ChatView },
        { path: "/saved", name: "saved", component: ChatView },
        { path: "/teams/:name", name: "team", component: ChatView, props: true },
        { path: "/directory", name: "directory", component: ChatView },

        {
            // OJO: el SPA usa /manage/* (NO /admin/*) para que sus rutas
            // no colisionen con los endpoints REST /admin/* del server.
            // Si las pusiéramos en /admin/users, F5 sobre esa URL haría
            // que ASP.NET sirviera el endpoint JSON (que requiere auth)
            // antes del fallback a index.html → 401 sin token.
            path: "/manage",
            component: AdminLayout,
            children: [
                { path: "users", name: "admin-users", component: AdminUsersView },
                { path: "license", name: "admin-license", component: AdminLicenseView },
                { path: "change-password", name: "admin-change-password", component: AdminChangePasswordView },
                { path: "auth-providers", name: "admin-auth-providers", component: AdminAuthProvidersView },
                { path: "auth-providers/:id/import", name: "admin-auth-provider-import", component: AdminAuthProviderImportView, props: true },
            ],
        },
        { path: "/:pathMatch(.*)*", redirect: "/" },
    ],
});

router.beforeEach((to: RouteLocationNormalized) => {
    const auth = useAuthStore();
    const isPublic = to.meta.public === true;
    if (!isPublic && !auth.isAuthenticated) {
        return { name: "login", query: { redirect: to.fullPath } };
    }
    if (isPublic && auth.isAuthenticated) {
        return { name: "inbox" };
    }
    return true;
});

setUnauthorizedHandler(() => {
    const auth = useAuthStore();
    auth.clear();
    if (router.currentRoute.value.name !== "login") {
        void router.replace({ name: "login" });
    }
});
