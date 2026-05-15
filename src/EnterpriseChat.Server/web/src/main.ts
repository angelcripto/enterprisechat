import { createApp } from "vue";
import { createPinia } from "pinia";
import App from "./App.vue";
import { router } from "./router";
import { useAuthStore } from "./stores/auth";
import "./style.css";

const app = createApp(App);
app.use(createPinia());

// Restaurar la sesión ANTES de instalar el router. El primer beforeEach
// evalúa isAuthenticated; si lo hacemos después, F5 sobre una ruta
// protegida lanza fetches sin Bearer y el server responde 401 antes de
// que onMounted de App.vue restaure el token.
useAuthStore().restoreFromStorage();

app.use(router);
app.mount("#app");
