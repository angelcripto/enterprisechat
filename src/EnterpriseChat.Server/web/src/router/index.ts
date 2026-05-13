import { createRouter, createWebHistory, type RouteLocationNormalized } from "vue-router";
import { useAuthStore } from "@/stores/auth";
import { setUnauthorizedHandler } from "@/api/client";

const LoginView = () => import("@/views/LoginView.vue");
const ChatView = () => import("@/views/ChatView.vue");

export const router = createRouter({
    history: createWebHistory(),
    routes: [
        { path: "/login", name: "login", component: LoginView, meta: { public: true } },
        { path: "/", name: "home", component: ChatView },
        { path: "/channels/:roomId", name: "channel", component: ChatView, props: true },
        { path: "/dm/:peerUserId", name: "dm", component: ChatView, props: true },
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
        return { name: "home" };
    }
    return true;
});

// When the API returns 401 we drop the session and force the login screen.
setUnauthorizedHandler(() => {
    const auth = useAuthStore();
    auth.clear();
    if (router.currentRoute.value.name !== "login") {
        void router.replace({ name: "login" });
    }
});
