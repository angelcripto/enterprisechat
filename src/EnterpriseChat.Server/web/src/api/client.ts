import axios, { AxiosError, type AxiosInstance } from "axios";

/**
 * Single axios instance for REST calls. SignalR has its own client and
 * authenticates separately via the access_token query string.
 *
 * The bearer token is injected on every request from the auth store via the
 * `setAccessToken` helper so the store can stay synchronous and avoid an
 * accessor function per call.
 */
let accessToken: string | null = null;

export function setAccessToken(token: string | null): void {
    accessToken = token;
}

export function getAccessToken(): string | null {
    return accessToken;
}

export const api: AxiosInstance = axios.create({
    baseURL: "/",
    timeout: 15000,
});

api.interceptors.request.use((config) => {
    if (accessToken !== null) {
        config.headers.Authorization = `Bearer ${accessToken}`;
    }
    return config;
});

let onUnauthorized: () => void = () => {};

export function setUnauthorizedHandler(handler: () => void): void {
    onUnauthorized = handler;
}

api.interceptors.response.use(
    (response) => response,
    (error: AxiosError) => {
        if (error.response?.status === 401) {
            onUnauthorized();
        }
        return Promise.reject(error);
    },
);
