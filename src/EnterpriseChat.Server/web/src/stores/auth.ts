import { defineStore } from "pinia";
import { computed, ref } from "vue";
import { api, setAccessToken } from "@/api/client";
import type { LoginRequest, LoginResponse, UserRole } from "@/api/types";

/**
 * Authentication state for the web client. The JWT lives in localStorage,
 * which is acceptable for a B2B internal tool: XSS is the only realistic
 * exfil path, and we mitigate it with Vue's autoescape, no v-html on
 * untrusted content, and a strict CSP at deploy time.
 */
const STORAGE_KEY = "ecat.session.v1";

export interface PersistedSession {
    accessToken: string;
    expiresAt: string;
    userId: number;
    username: string;
    fullName: string;
    role: UserRole;
}

export const useAuthStore = defineStore("auth", () => {
    const session = ref<PersistedSession | null>(null);

    const isAuthenticated = computed(() => session.value !== null && !isExpired());
    const userId = computed(() => session.value?.userId ?? null);
    const username = computed(() => session.value?.username ?? "");
    const fullName = computed(() => session.value?.fullName ?? "");
    const role = computed<UserRole | null>(() => session.value?.role ?? null);
    const isAdmin = computed(() => session.value?.role === "Admin");

    function isExpired(): boolean {
        if (session.value === null) return true;
        return new Date(session.value.expiresAt).getTime() < Date.now();
    }

    function restoreFromStorage(): void {
        try {
            const raw = localStorage.getItem(STORAGE_KEY);
            if (raw === null) return;
            const parsed = JSON.parse(raw) as PersistedSession;
            session.value = parsed;
            setAccessToken(parsed.accessToken);
            if (isExpired()) {
                clear();
            }
        } catch {
            clear();
        }
    }

    function persist(s: PersistedSession): void {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(s));
    }

    async function login(req: LoginRequest): Promise<void> {
        const { data } = await api.post<LoginResponse>("/auth/login", req);
        const s: PersistedSession = {
            accessToken: data.accessToken,
            expiresAt: data.expiresAt,
            userId: data.userId,
            username: data.username,
            fullName: data.fullName,
            role: data.role,
        };
        session.value = s;
        setAccessToken(s.accessToken);
        persist(s);
    }

    function clear(): void {
        session.value = null;
        setAccessToken(null);
        localStorage.removeItem(STORAGE_KEY);
    }

    return {
        session,
        isAuthenticated,
        userId,
        username,
        fullName,
        role,
        isAdmin,
        restoreFromStorage,
        login,
        clear,
    };
});
