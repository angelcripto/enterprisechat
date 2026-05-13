import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import tailwindcss from "@tailwindcss/vite";
import path from "node:path";

// The Vue SPA builds straight into the server's wwwroot so a single
// `dotnet publish` outputs one binary that serves API + SignalR + UI.
// `emptyOutDir: true` is safe here because wwwroot only contains the SPA
// artefacts; .gitkeep is preserved by being committed and re-created at
// build time when Vite copies any `public/` content.
export default defineConfig({
    plugins: [vue(), tailwindcss()],
    resolve: {
        alias: {
            "@": path.resolve(__dirname, "./src"),
        },
    },
    build: {
        outDir: "../wwwroot",
        emptyOutDir: true,
        sourcemap: false,
        chunkSizeWarningLimit: 800,
    },
    server: {
        port: 5173,
        strictPort: true,
        // The Vue dev server proxies API + hub calls to the running C# server
        // so the SPA can talk to it without CORS during development. The C#
        // server listens on http://localhost:5080 by default.
        proxy: {
            "/auth": "http://localhost:5080",
            "/users": "http://localhost:5080",
            "/rooms": "http://localhost:5080",
            "/search": "http://localhost:5080",
            "/files": "http://localhost:5080",
            "/admin": "http://localhost:5080",
            "/license": "http://localhost:5080",
            "/healthz": "http://localhost:5080",
            "/hubs": {
                target: "http://localhost:5080",
                ws: true,
                changeOrigin: true,
            },
        },
    },
});
