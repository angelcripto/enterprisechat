import { createRouter, createWebHistory, type RouteLocationNormalized } from "vue-router";
import { useAuthStore } from "@/stores/auth";
import { setUnauthorizedHandler } from "@/api/client";

const LoginView = () => import("@/views/LoginView.vue");
const ChatView = () => import("@/views/ChatView.vue");
const AdminLicenseView = () => import("@/views/AdminLicenseView.vue");
const AdminAuthProvidersView = () => import("@/views/AdminAuthProvidersView.vue");

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

        { path: "/admin/license", name: "admin-license", component: AdminLicenseView },
        { path: "/admin/auth-providers", name: "admin-auth-providers", component: AdminAuthProvidersView },
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
