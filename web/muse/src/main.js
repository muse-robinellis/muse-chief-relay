import { createApp, h, ref } from "vue";
import App from "./App.vue";
import WatchLive from "./WatchLive.vue";
import { isWatchRoute } from "./watchRoute.js";
import "./styles.css";

// Tiny hash router. Exactly "#/watch" (or "#/watch/") mounts the read-only
// live spectator view; anything else, including "#/watchdog", mounts the
// interactive client. The hash is only ever read, never written, and
// switching views unmounts the previous root cleanly.
function pick() {
  return isWatchRoute(window.location.hash) ? WatchLive : App;
}

const current = ref(pick());
window.addEventListener("hashchange", () => {
  current.value = pick();
});

createApp({ setup: () => () => h(current.value) }).mount("#app");
